using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Diagnostics;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.TeklaShared.Extensions;
using Speckle.Connectors.TeklaShared.HostApp;
using Speckle.Converters.Common;
using Speckle.Converters.TeklaShared;
using Speckle.Objects.Data;
using Speckle.Objects.Utils;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.Models;
using Speckle.Sdk.Pipelines;
using Speckle.Sdk.Pipelines.Progress;
using Speckle.Sdk.Pipelines.Send.Artifacts;
using DataObject = Speckle.Objects.Data.DataObject; // disambiguate from System.Windows.Forms.DataObject
using Path = System.IO.Path;
using SpecContainer = Speckle.Bundle.Spec.Container;
using SpecMaterial = Speckle.Bundle.Spec.Material;

namespace Speckle.Connectors.TeklaShared.Operations.Send;

/// <summary>
/// Speckle 4.0 send path for Tekla: instead of assembling a <see cref="Speckle.Sdk.Models.Collections.Collection"/>
/// graph of <see cref="TeklaObject"/>s and serializing it, this converts each model object and drives the SDK
/// <see cref="ObjectsArtifactPipeline"/> to write the client-side artefact triple directly — <c>geometries.parquet</c>
/// (SGEO meshes/curves), <c>eav.*.parquet</c> (properties), <c>envelope.*.parquet</c> (the by-type collection graph +
/// render materials) — then uploads via <see cref="ArtifactPipeline"/>.
/// </summary>
/// <remarks>
/// <para>Registering this builder in <c>AddTekla</c> is the only switch — <c>SendOperation</c> auto-selects
/// <c>SendViaArtifacts</c>; no binding/UX change.</para>
/// <para>Tekla is render-mesh based: parts tessellate to <see cref="Speckle.Objects.Geometry.Mesh"/> (rebar/grids to
/// lines/arcs), all SGEO-encodable, with no lossless raw solid — so this emits only <c>DISPLAY</c> geometry (no
/// <c>SOLID</c>) and no instances (Tekla has none). Grouping is flat by object type. Nested <c>TeklaObject.elements</c>
/// (bolts, sub-parts, rebar, …) are interned as their own objects (own geometry + properties, so
/// <see cref="TeklaMaterialUnpacker"/>'s per-applicationId <c>HAS_MATERIAL</c> edges resolve) and linked to their
/// owner with a <c>SUBELEMENT</c> edge, while still appearing in the flat by-type scene collection. Assemblies, pour
/// units and pour objects are geometry-less objects reached from the parts: every part points at its assembly with
/// <c>IN_ASSEMBLY</c> (main part ord 0, the rest in Tekla's member order from 1, nested assemblies the same way) and
/// every pour object at its pour unit (ord 0 for all, ADR-0003 of the atlas spec, ENG-11151). Tekla sends no
/// analysis results.</para>
/// <para><b>Threading.</b> Two-phase like Rhino: the Tekla API (<c>GetSolid</c>/report properties/visualisation) is
/// main-thread-affine, so phase 1 converts on the host thread → a pure-Speckle snapshot; phase 2 builds the parquet
/// bundle on a worker (the pipeline's sync-over-async IO deadlocks on a UI SynchronizationContext).</para>
/// </remarks>
public class TeklaArtifactRootObjectBuilder(
  IRootToSpeckleConverter converter,
  IConverterSettingsStore<TeklaConversionSettings> converterSettings,
  TeklaMaterialUnpacker materialUnpacker,
  IThreadContext threadContext,
  IArtifactPipelineFactory artifactPipelineFactory,
  ISpeckleApplication speckleApplication,
  ILogger<TeklaArtifactRootObjectBuilder> logger
) : IArtifactRootObjectBuilder<TSM.ModelObject>
{
  public async Task<ArtifactBuildResult> BuildAndUpload(
    IReadOnlyList<TSM.ModelObject> objects,
    string projectId,
    string ingestionId,
    string versionId,
    Account account,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    var outputDir = Path.Combine(Path.GetTempPath(), "Speckle", "artifacts", versionId);
    System.IO.Directory.CreateDirectory(outputDir);

    using var session = ArtefactSessionLog.Start("Tekla", ArtefactDirection.Send, projectId, null, versionId, logger);

    // Phase 1 — convert on the host thread (Tekla API is main-thread-affine) → pure-Speckle snapshot.
    CollectedModel collected;
    using (session.Phase("Collect"))
    {
      collected = await threadContext.RunOnMainAsync(() =>
        Task.FromResult(CollectOnMain(objects, session, onOperationProgressed, cancellationToken))
      );
    }

    // Phase 2 — build + upload on a worker thread.
    return await threadContext.RunOnWorkerAsync(async () =>
    {
      BundleResult built;
      using (session.Phase("Write"))
      {
        built = WriteBundle(collected, session, versionId, outputDir, onOperationProgressed, cancellationToken);
      }

      using var pipeline = artifactPipelineFactory.CreateInstance(
        projectId,
        ingestionId,
        versionId,
        account,
        outputDir,
        cancellationToken
      );

      onOperationProgressed.Report(new("Uploading...", null));
      string finalVersionId;
      using (session.Phase("Upload"))
      {
        finalVersionId = await pipeline
          .UploadFilesAsync(built.Bundle, built.RootId, built.ObjectCount)
          .ConfigureAwait(false);
      }

      return new ArtifactBuildResult(finalVersionId, built.RootId, built.Results);
    });
  }

  // ── Phase 1 (host thread): Tekla API → pure-Speckle snapshot ──────────────────────────────────────────
  private CollectedModel CollectOnMain(
    IReadOnlyList<TSM.ModelObject> objects,
    ArtefactSessionLog session,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    var units = converterSettings.Current.SpeckleUnits;
    var expanded = ExpandSelection(objects);
    var collected = new List<CollectedObject>(expanded.Count);
    var results = new List<SendConversionResult>(expanded.Count);

    int count = 0;
    foreach (TSM.ModelObject modelObject in expanded)
    {
      cancellationToken.ThrowIfCancellationRequested();
      string sourceType = modelObject.GetType().ToString().Split('.').Last();
      var sw = Stopwatch.StartNew();
      try
      {
        Base converted = converter.Convert(modelObject);
        string appId = converted.applicationId ?? Guid.NewGuid().ToString();
        collected.Add(new CollectedObject(sourceType, converted));
        results.Add(new(Status.SUCCESS, appId, sourceType, converted));
        session.RecordObject(appId, sourceType, Status.SUCCESS, null, sw.ElapsedMilliseconds);
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        logger.LogError(ex, "Failed to convert {SourceType}", sourceType);
        results.Add(new(Status.ERROR, sourceType, sourceType, null, ex));
        session.RecordObject(sourceType, sourceType, Status.ERROR, ex.Message, sw.ElapsedMilliseconds);
      }

      onOperationProgressed.Report(new("Converting", (double)++count / expanded.Count));
    }

    if (results.Count > 0 && results.All(x => x.Status == Status.ERROR))
    {
      throw new SpeckleException("Failed to convert all objects.");
    }

    var memberships = CollectMemberships(expanded, collected, results, session);

    // Render materials (Tekla visualisation colours), keyed by object applicationId — needs the Tekla API, so phase 1.
    var materials = materialUnpacker.UnpackRenderMaterial(expanded);

    return new CollectedModel(units, collected, materials, results, memberships);
  }

  // A Tekla selection can hold assemblies and pour units themselves. They are membership targets here, not owners
  // (ENG-11151), so a selected one stands for its members; it still reaches the bundle through the membership walk.
  private static List<TSM.ModelObject> ExpandSelection(IReadOnlyList<TSM.ModelObject> objects)
  {
    var expanded = new List<TSM.ModelObject>(objects.Count);
    var seen = new HashSet<Guid>();
    foreach (TSM.ModelObject modelObject in objects)
    {
      Add(modelObject);
    }
    return expanded;

    void Add(TSM.ModelObject modelObject)
    {
      switch (modelObject)
      {
        case TSM.Assembly assembly:
          foreach (TSM.ModelObject member in AssemblyMembers(assembly))
          {
            Add(member);
          }
          break;
        case TSM.PourUnit pourUnit:
          foreach (TSM.ModelObject member in pourUnit.GetObjects())
          {
            Add(member);
          }
          break;
        default:
          if (seen.Add(modelObject.Identifier.GUID))
          {
            expanded.Add(modelObject);
          }
          break;
      }
    }
  }

  // Tekla's member order: the main object first, then the secondaries, then any sub-assembly the secondaries list
  // left out. That position is the IN_ASSEMBLY ord.
  private static IEnumerable<TSM.ModelObject> AssemblyMembers(TSM.Assembly assembly)
  {
    var seen = new HashSet<Guid>();
    if (assembly.GetMainObject() is TSM.ModelObject main && seen.Add(main.Identifier.GUID))
    {
      yield return main;
    }
    foreach (TSM.ModelObject secondary in assembly.GetSecondaries())
    {
      if (seen.Add(secondary.Identifier.GUID))
      {
        yield return secondary;
      }
    }
    foreach (TSM.ModelObject subAssembly in assembly.GetSubAssemblies())
    {
      if (seen.Add(subAssembly.Identifier.GUID))
      {
        yield return subAssembly;
      }
    }
  }

  // Walks the sent objects (and their children, the same walk the converter does) for parts, and derives the
  // assemblies, pour objects and pour units they belong to. Each derived object converts once and is collected like a
  // sent object; the edges are resolved against application ids in phase 2.
  private List<Membership> CollectMemberships(
    IReadOnlyList<TSM.ModelObject> roots,
    List<CollectedObject> collected,
    List<SendConversionResult> results,
    ArtefactSessionLog session
  )
  {
    var edges = new List<Membership>();
    var visited = new HashSet<Guid>();
    var derived = new Dictionary<Guid, string>();
    var memberOrds = new Dictionary<Guid, Dictionary<Guid, int>>();
    foreach (TSM.ModelObject root in roots)
    {
      Walk(root);
    }
    return edges;

    void Walk(TSM.ModelObject modelObject)
    {
      if (!visited.Add(modelObject.Identifier.GUID))
      {
        return;
      }
      if (modelObject is TSM.Part part)
      {
        RecordPart(part);
      }
      foreach (TSM.ModelObject child in modelObject.GetSupportedChildren())
      {
        Walk(child);
      }
    }

    void RecordPart(TSM.Part part)
    {
      try
      {
        if (part.GetAssembly() is TSM.Assembly assembly)
        {
          edges.Add(new Membership(part.GetSpeckleApplicationId(), Derive(assembly), OrdIn(assembly, part)));
        }
        foreach (TSM.ModelObject pour in part.GetPours())
        {
          if (pour is TSM.PourObject pourObject && pourObject.GetFatherPourUnit() is TSM.PourUnit pourUnit)
          {
            edges.Add(new Membership(Derive(pourObject), Derive(pourUnit), 0));
          }
        }
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        logger.LogWarning(ex, "Skipped assembly and pour membership of {AppId}", part.GetSpeckleApplicationId());
      }
    }

    // Converts a membership target once; an assembly also points at its parent assembly.
    string Derive(TSM.ModelObject target)
    {
      Guid guid = target.Identifier.GUID;
      if (derived.TryGetValue(guid, out string? known))
      {
        return known;
      }
      string sourceType = target.GetType().Name;
      var sw = Stopwatch.StartNew();
      Base converted = converter.Convert(target);
      string appId = converted.applicationId ?? guid.ToString();
      derived[guid] = appId;
      collected.Add(new CollectedObject(sourceType, converted));
      results.Add(new(Status.SUCCESS, appId, sourceType, converted));
      session.RecordObject(appId, sourceType, Status.SUCCESS, null, sw.ElapsedMilliseconds);
      if (target is TSM.Assembly assembly && assembly.GetAssembly() is TSM.Assembly parent)
      {
        edges.Add(new Membership(appId, Derive(parent), OrdIn(parent, assembly)));
      }
      return appId;
    }

    int OrdIn(TSM.Assembly assembly, TSM.ModelObject member)
    {
      Guid guid = assembly.Identifier.GUID;
      if (!memberOrds.TryGetValue(guid, out var ords))
      {
        ords = new Dictionary<Guid, int>();
        int ord = 0;
        foreach (TSM.ModelObject listed in AssemblyMembers(assembly))
        {
          ords[listed.Identifier.GUID] = ord++;
        }
        memberOrds[guid] = ords;
      }
      // A member the API does not list goes after every listed one, never in the main slot.
      return ords.TryGetValue(member.Identifier.GUID, out int found) ? found : ords.Count;
    }
  }

  // ── Phase 2 (worker thread): snapshot → parquet bundle ────────────────────────────────────────────────
  [SuppressMessage("Maintainability", "CA1506:Avoid excessive class coupling")]
  private BundleResult WriteBundle(
    CollectedModel model,
    ArtefactSessionLog session,
    string versionId,
    string outputDir,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    ZstdNativeLoader.Ensure(logger); // net48: ensure the parquet Zstd native is loaded (no-op on net8+)
    using var pipeline = new ObjectsArtifactPipeline(outputDir, versionId, producer: speckleApplication);
    var collectionKByName = new Dictionary<string, int>(StringComparer.Ordinal);
    var geometryKsByAppId = new Dictionary<string, List<int>>(StringComparer.Ordinal);

    int count = 0;
    foreach (CollectedObject co in model.Objects)
    {
      cancellationToken.ThrowIfCancellationRequested();
      int collK = GetOrAddCollection(pipeline, co.CollectionName, collectionKByName);
      EmitObject(pipeline, co.Converted, collK, model.Units, geometryKsByAppId);
      onOperationProgressed.Report(new("Building", (double)++count / model.Objects.Count));
    }

    EmitMaterials(pipeline, model.Materials, geometryKsByAppId);

    foreach (Membership membership in model.Memberships)
    {
      pipeline.InAssembly(
        pipeline.InternObject(membership.MemberAppId),
        pipeline.InternObject(membership.AssemblyAppId),
        membership.Ord
      );
    }

    // Default scene view: the flat by-type collections (IN_COLLECTION).
    pipeline.AddSceneView(new SceneView(0, "Default", true, new[] { SceneViewKey.Rel(RelKind.InCollection) }));

    pipeline.Complete();

    var bundle = System
      .IO.Directory.EnumerateFiles(outputDir, versionId + ".*")
      .Where(p => p.EndsWith(".parquet", StringComparison.Ordinal))
      .ToDictionary(p => Path.GetFileName(p)!, p => p, StringComparer.Ordinal);

    var objectCount = model.Results.Count(r => r.Status == Status.SUCCESS);
    var rootId = $"binary-{versionId}";

    session.SetStat("files", bundle.Count);
    session.SetStat("objects", objectCount);
    session.SetStat("materials", model.Materials.Count);
    return new BundleResult(bundle, rootId, objectCount, model.Results);
  }

  // Emits one object (and recursively its TeklaObject children). Children are interned as their own objects (own
  // geometry + properties) and linked to the parent with a SUBELEMENT edge (part → bolts / rebar / sub-parts), while
  // still appearing in the flat by-type scene collection. <paramref name="parentObjK"/> is the owner's object K when
  // this call is a child.
  private void EmitObject(
    ObjectsArtifactPipeline pipeline,
    Base obj,
    int collK,
    string units,
    Dictionary<string, List<int>> geometryKsByAppId,
    int? parentObjK = null,
    int subelementOrd = 0
  )
  {
    string appId = obj.applicationId ?? Guid.NewGuid().ToString();
    int objK = pipeline.InternObject(appId);
    pipeline.InCollection(objK, collK, 0);
    if (parentObjK is int pK)
    {
      pipeline.Subelement(pK, objK, subelementOrd);
    }

    var display = obj is DataObject dataObject ? dataObject.displayValue : new List<Base> { obj };
    var properties = obj is DataObject d ? d.properties : null;
    string type = obj["type"]?.ToString() ?? obj.speckle_type;
    pipeline.AddProperties(
      appId,
      properties ?? s_emptyProps,
      RootScalars(obj.speckle_type, obj["name"]?.ToString(), units, type)
    );

    var gKs = geometryKsByAppId[appId] = new List<int>();
    int ord = 0;
    foreach (Base fragment in display)
    {
      try
      {
        string gAppId = fragment.applicationId ?? $"{appId}:g{ord}";
        int gK = pipeline.AddGeometry(gAppId, fragment);
        pipeline.Display(objK, gK, ord++);
        gKs.Add(gK);
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        logger.LogWarning(ex, "Skipped unsupported display geometry {Type} on {AppId}", fragment.speckle_type, appId);
      }
    }

    if (obj is TeklaObject teklaObject)
    {
      int childOrd = 0;
      foreach (TeklaObject child in teklaObject.elements)
      {
        EmitObject(pipeline, child, collK, units, geometryKsByAppId, objK, childOrd++);
      }
    }
  }

  private static void EmitMaterials(
    ObjectsArtifactPipeline pipeline,
    IReadOnlyList<Speckle.Objects.Other.RenderMaterialProxy> materials,
    Dictionary<string, List<int>> geometryKsByAppId
  )
  {
    foreach (var materialProxy in materials)
    {
      var value = materialProxy.value;
      int matK = pipeline.AddMaterial(
        materialProxy.applicationId.NotNull(),
        new SpecMaterial(
          value.name,
          value.diffuse,
          value.opacity,
          value.metalness,
          value.roughness,
          value.emissive,
          value["ior"] as double? // dynamic prop (v1 unpacker convention); null when the host has no IOR [ENG-8791]
        )
      );
      foreach (var objectId in materialProxy.objects)
      {
        if (geometryKsByAppId.TryGetValue(objectId, out var gKs))
        {
          foreach (var gK in gKs)
          {
            pipeline.HasMaterial(gK, matK);
          }
        }
      }
    }
  }

  private static int GetOrAddCollection(ObjectsArtifactPipeline pipeline, string name, Dictionary<string, int> cache)
  {
    if (cache.TryGetValue(name, out var existing))
    {
      return existing;
    }
    int collK = pipeline.AddCollection(name, new SpecContainer(name, null, "Collection", null));
    cache[name] = collK;
    return collK;
  }

  private static readonly Dictionary<string, object?> s_emptyProps = new();

  private static KeyValuePair<string, object?>[] RootScalars(
    string speckleType,
    string? name,
    string units,
    string sourceType
  ) =>
    new KeyValuePair<string, object?>[]
    {
      new("speckle_type", speckleType),
      new("name", name),
      new("units", units),
      new("type", sourceType),
    };

  private sealed record CollectedObject(string CollectionName, Base Converted);

  // IN_ASSEMBLY member → assembly (or pour object → pour unit), by application id.
  private sealed record Membership(string MemberAppId, string AssemblyAppId, int Ord);

  private sealed record CollectedModel(
    string Units,
    IReadOnlyList<CollectedObject> Objects,
    IReadOnlyList<Speckle.Objects.Other.RenderMaterialProxy> Materials,
    IReadOnlyList<SendConversionResult> Results,
    IReadOnlyList<Membership> Memberships
  );

  private sealed record BundleResult(
    IReadOnlyDictionary<string, string> Bundle,
    string RootId,
    int ObjectCount,
    IReadOnlyList<SendConversionResult> Results
  );
}
