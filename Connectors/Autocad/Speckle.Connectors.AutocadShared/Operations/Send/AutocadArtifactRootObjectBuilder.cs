using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
#if NETFRAMEWORK
using System.IO; // net8+ provides this via ImplicitUsings; net48 needs it explicitly.
#endif
using Autodesk.AutoCAD.DatabaseServices;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Autocad.HostApp;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Diagnostics;
using Speckle.Connectors.Common.Threading;
using Speckle.Converters.Autocad;
using Speckle.Converters.Common;
using Speckle.DoubleNumerics;
using Speckle.Objects.Data;
using Speckle.Objects.Other;
using Speckle.Objects.Utils;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Instances;
using Speckle.Sdk.Models.Proxies;
using Speckle.Sdk.Pipelines.Progress;
using Speckle.Sdk.Pipelines.Send.Artifacts;

namespace Speckle.Connectors.Autocad.Operations.Send;

/// <summary>
/// Speckle 4.0 send path for AutoCAD (+ Civil3D / Plant3D verticals): instead of building a
/// <see cref="Speckle.Sdk.Models.Collections.Collection"/> graph of <see cref="AutocadObject"/>s and serializing it
/// through the v1 pipeline, this drives the SDK <see cref="ObjectsArtifactPipeline"/> to write the client-side
/// artefact triple directly — <c>geometries.parquet</c> (SGEO blobs + raw ACIS-SAT solid blobs),
/// <c>eav.*.parquet</c> (properties), <c>envelope.*.parquet</c> (relations + value-node topology) — then uploads
/// the bundle via <see cref="ArtifactPipeline"/>.
/// </summary>
/// <remarks>
/// <para>Mirrors <c>RhinoArtifactRootObjectBuilder</c>. Two AutoCAD-specific shapes:</para>
/// <list type="number">
///   <item><b>Solids.</b> A <c>Solid3d</c> is kept losslessly as the raw ACIS-SAT blob the converter already
///   produced (<see cref="AutocadObject.rawEncoding"/>, base64 SAT). It is written to <c>geometries.parquet</c>
///   verbatim with <c>type = "sat"</c> and linked via the <c>SOLID</c> rel, alongside its display meshes which
///   link via <c>DISPLAY</c>.</item>
///   <item><b>Layers are flat.</b> Unlike Rhino's nested layer tree, AutoCAD has a flat layer namespace — each
///   layer becomes a single top-level <c>COLLECTION</c> node, with an <c>IN_COLLECTION</c> rel per object; the
///   default scene view projects on <c>IN_COLLECTION</c>.</item>
/// </list>
/// <para><b>Threading.</b> Two phases, exactly as Rhino: phase 1 (<see cref="CollectOnMain"/>) runs on the host UI
/// thread (the AutoCAD API + converters are document-bound) producing a pure-Speckle snapshot; phase 2
/// (<see cref="WriteBundle"/>) runs on a worker thread and builds the parquet bundle (the pipeline does
/// sync-over-async parquet IO that deadlocks on the UI thread).</para>
/// <para>The single builder serves AutoCAD, Civil3D and Plant3D: the injected <see cref="IRootToSpeckleConverter"/>
/// and unpackers resolve to each vertical's registrations, so per-vertical geometry/property extraction is correct
/// without subclassing. Deferred this pass: AutoCAD groups, Civil3D property-set definitions.</para>
/// </remarks>
public class AutocadArtifactRootObjectBuilder(
  IRootToSpeckleConverter converter,
  IConverterSettingsStore<AutocadConversionSettings> converterSettings,
  AutocadInstanceUnpacker instanceUnpacker,
  AutocadMaterialUnpacker materialUnpacker,
  AutocadColorUnpacker colorUnpacker,
  IThreadContext threadContext,
  IArtifactPipelineFactory artifactPipelineFactory,
  ILogger<AutocadArtifactRootObjectBuilder> logger
) : IArtifactRootObjectBuilder<AutocadRootObject>
{
  public async Task<ArtifactBuildResult> BuildAndUpload(
    IReadOnlyList<AutocadRootObject> objects,
    string projectId,
    string ingestionId,
    string versionId,
    Account account,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    // Bundle base name = the server pre-allocated versionId, so the parquet files carry their final names from byte one.
    var outputDir = Path.Combine(Path.GetTempPath(), "Speckle", "artifacts", versionId);
    Directory.CreateDirectory(outputDir);

    using var session = ArtefactSessionLog.Start("Autocad", ArtefactDirection.Send, projectId, null, versionId, logger);

    // Phase 1 — convert + unpack on the host UI thread (AutoCAD is document/main-thread-affine) → pure-Speckle snapshot.
    CollectedModel collected;
    using (session.Phase("Collect"))
    {
      collected = await threadContext.RunOnMainAsync(
        () => Task.FromResult(CollectOnMain(objects, session, onOperationProgressed, cancellationToken))
      );
    }

    // Phase 2 — build the parquet bundle + upload on a WORKER thread (no UI SynchronizationContext, so the
    // pipeline's sync-over-async parquet IO doesn't deadlock — see the class <remarks>).
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

  // ── Phase 1 (UI thread): AutoCAD API → pure-Speckle snapshot ──────────────────────────────────────────
  private CollectedModel CollectOnMain(
    IReadOnlyList<AutocadRootObject> objects,
    ArtefactSessionLog session,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    var units = converterSettings.Current.SpeckleUnits;

    // Unpack instances → atomic objects (incl. block-definition members) + instance/definition proxies.
    var (atomicObjects, _, instanceProxies, instanceDefinitionProxies) = instanceUnpacker.UnpackSelection(objects);

    var collectedObjects = new List<CollectedObject>(atomicObjects.Count);
    var results = new List<SendConversionResult>(atomicObjects.Count);

    int count = 0;
    foreach (var (entity, applicationId) in atomicObjects)
    {
      cancellationToken.ThrowIfCancellationRequested();
      string sourceType = entity.GetType().Name;
      var sw = Stopwatch.StartNew();
      try
      {
        CollectedObject collected = CollectObject(entity, applicationId, sourceType, instanceProxies);
        collectedObjects.Add(collected);
        results.Add(new(Status.SUCCESS, applicationId, sourceType, collected.Converted));
        session.RecordObject(applicationId, sourceType, Status.SUCCESS, null, sw.ElapsedMilliseconds);
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        logger.LogError(ex, "Failed to convert {SourceType}", sourceType);
        results.Add(new(Status.ERROR, applicationId, sourceType, null, ex));
        session.RecordObject(applicationId, sourceType, Status.ERROR, ex.Message, sw.ElapsedMilliseconds);
      }

      onOperationProgressed.Report(new("Converting", (double)++count / atomicObjects.Count));
    }

    if (results.Count > 0 && results.All(x => x.Status == Status.ERROR))
    {
      throw new SpeckleException("Failed to convert all objects.");
    }

    // Materials/colors are keyed by name and list OBJECT ids (resolved to geometry K(s) in phase 2). Layer-level
    // materials/colors are skipped this pass (object-level only) — pass an empty layer list.
    var materials = materialUnpacker.UnpackMaterials(atomicObjects, new List<LayerTableRecord>());
    var colors = colorUnpacker.UnpackColors(atomicObjects, new List<LayerTableRecord>());

    return new CollectedModel(units, collectedObjects, materials, colors, instanceDefinitionProxies, results);
  }

  // Converts one AutoCAD entity to its Speckle representation (instance proxy or AutocadObject carrier). The
  // AutocadObject is a transient carrier — phase 2 reads its displayValue/rawEncoding/properties and never serializes
  // it (the same way Rhino transiently uses InstanceProxy/RenderMaterialProxy).
  private CollectedObject CollectObject(
    Entity entity,
    string applicationId,
    string sourceType,
    IReadOnlyDictionary<string, InstanceProxy> instanceProxies
  )
  {
    if (instanceProxies.TryGetValue(applicationId, out InstanceProxy? instanceProxy))
    {
      var instanceProps = instanceProxy["properties"] as Dictionary<string, object?> ?? new Dictionary<string, object?>();
      return new CollectedObject(applicationId, sourceType, entity.Layer, instanceProps, instanceProxy);
    }

    Base converted = converter.Convert(entity);
    // AutoCAD wraps entities in AutocadObject, Civil3D in Civil3dObject — both are DataObjects carrying the
    // converted properties + display meshes.
    var properties = converted is DataObject dataObject ? dataObject.properties : new Dictionary<string, object?>();
    return new CollectedObject(applicationId, sourceType, entity.Layer, properties, converted);
  }

  // ── Phase 2 (worker thread): pure-Speckle snapshot → parquet bundle ──────────────────────────────────
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
    AutocadZstdNativeLoader.Ensure(logger); // net48: ensure the parquet Zstd native is loaded (no-op on net8+)
    using var pipeline = new ObjectsArtifactPipeline(outputDir, versionId);

    // Pre-create DEFINITION nodes so they carry their proper name (the per-object pass only has the definitionId).
    foreach (var defProxy in model.Definitions)
    {
      pipeline.AddDefinition(defProxy.applicationId.NotNull(), defProxy.name);
    }

    var layerCollectionKByName = new Dictionary<string, int>(StringComparer.Ordinal);
    var geometryKsByObjectId = new Dictionary<string, List<int>>(StringComparer.Ordinal);
    var instanceKByObjectId = new Dictionary<string, int>(StringComparer.Ordinal);

    int count = 0;
    foreach (CollectedObject co in model.Objects)
    {
      cancellationToken.ThrowIfCancellationRequested();
      int collK = GetOrAddLayerCollection(pipeline, co.LayerName, layerCollectionKByName);
      EmitObject(pipeline, co, collK, model.Units, geometryKsByObjectId, instanceKByObjectId);
      onOperationProgressed.Report(new("Building", (double)++count / model.Objects.Count));
    }

    EmitValueNodes(pipeline, model, geometryKsByObjectId, instanceKByObjectId);

    // Default scene view: the (flat) AutoCAD layer namespace via IN_COLLECTION.
    pipeline.AddSceneView(new SceneView(0, "Default", true, new[] { SceneViewKey.Rel(RelKind.InCollection) }));

    pipeline.Complete();

    var bundle = Directory
      .EnumerateFiles(outputDir, versionId + ".*")
      .Where(p => p.EndsWith(".parquet", StringComparison.Ordinal))
      .ToDictionary(p => Path.GetFileName(p)!, p => p, StringComparer.Ordinal);

    var objectCount = model.Results.Count(r => r.Status == Status.SUCCESS);
    var rootId = $"binary-{versionId}";

    session.SetStat("files", bundle.Count);
    session.SetStat("objects", objectCount);
    session.SetStat("definitions", model.Definitions.Count);
    session.SetStat("materials", model.Materials.Count);
    session.SetStat("layers", layerCollectionKByName.Count);

    logger.LogInformation("Built artefact bundle: {fileCount} files, {objectCount} objects", bundle.Count, objectCount);
    return new BundleResult(bundle, rootId, objectCount, model.Results);
  }

  // Emits one object: eav labels + IN_COLLECTION, then a block placement (DISPLAY_INSTANCE → INSTANCE node) or
  // geometry — the lossless SAT SOLID blob (if present) plus the DISPLAY meshes. Pure Speckle (no AutoCAD API).
  private void EmitObject(
    ObjectsArtifactPipeline pipeline,
    CollectedObject co,
    int collK,
    string units,
    Dictionary<string, List<int>> geometryKsByObjectId,
    Dictionary<string, int> instanceKByObjectId
  )
  {
    int objK = pipeline.InternObject(co.ApplicationId);
    pipeline.InCollection(objK, collK, 0);
    pipeline.AddProperties(co.ApplicationId, co.Properties, RootScalars(co.Converted.speckle_type, co.SourceType, units, co.SourceType));

    // ── block instance: object → INSTANCE node (transform + definition) via DISPLAY_INSTANCE ──────────
    if (co.Converted is InstanceProxy instanceProxy)
    {
      int defK = pipeline.AddDefinition(instanceProxy.definitionId, null);
      int instK = pipeline.AddInstance(co.ApplicationId, defK, Flatten(instanceProxy.transform), instanceProxy.units);
      pipeline.DisplayInstance(objK, instK, 0);
      instanceKByObjectId[co.ApplicationId] = instK;
      return;
    }

    // ── geometry object ───────────────────────────────────────────────────────────────────────────────
    // The DataObject carrier (AutocadObject / Civil3dObject) already split display meshes from the lossless raw
    // encoding (a Solid3d carries both; a plain mesh/curve/point is its own display). AutoCAD solids carry an
    // ACIS-SAT rawEncoding; Civil3D objects carry their base curve(s) which are also renderable geometry.
    List<Base> displayGeometry;
    RawEncoding? rawEncoding = null;
    if (co.Converted is DataObject dataObject)
    {
      displayGeometry = new List<Base>(dataObject.displayValue);
      if (co.Converted is Civil3dObject civil && civil.baseCurves is { Count: > 0 } baseCurves)
      {
        displayGeometry.AddRange(baseCurves.Cast<Base>());
      }
      rawEncoding = (co.Converted as AutocadObject)?.rawEncoding;
    }
    else
    {
      displayGeometry = new List<Base> { co.Converted };
    }

    // Authoritative solid: the raw ACIS-SAT blob, kept verbatim for receive-as-solids.
    if (rawEncoding is not null && rawEncoding.format == RawEncodingFormats.ACAD_SAT)
    {
      byte[] solidBytes = Convert.FromBase64String(rawEncoding.contents);
      int solidK = pipeline.AddRawGeometry($"{co.ApplicationId}:solid", solidBytes, RawEncodingFormats.ACAD_SAT);
      pipeline.Solid(objK, solidK, 0);
    }

    // Renderable display meshes (and self-display primitives the SGEO encoder supports: points/curves).
    var gKs = new List<int>();
    int ord = 0;
    foreach (Base fragment in displayGeometry)
    {
      try
      {
        string gAppId = fragment.applicationId ?? $"{co.ApplicationId}:g{ord}";
        int gK = pipeline.AddGeometry(gAppId, fragment);
        pipeline.Display(objK, gK, ord++);
        gKs.Add(gK);
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        logger.LogWarning(ex, "Skipped unsupported display geometry {Type} on {AppId}", fragment.speckle_type, co.ApplicationId);
      }
    }

    geometryKsByObjectId[co.ApplicationId] = gKs;
  }

  // Definition members (DEFINES / DEFINES_INSTANCE) → render materials (HAS_MATERIAL) → object colors (HAS_COLOR).
  // Order matters: all referenced meshes/instances must exist (added in the object loop) before the edges resolve them.
  private static void EmitValueNodes(
    ObjectsArtifactPipeline pipeline,
    CollectedModel model,
    Dictionary<string, List<int>> geometryKsByObjectId,
    Dictionary<string, int> instanceKByObjectId
  )
  {
    // 1) instance definitions → DEFINES (member meshes) / DEFINES_INSTANCE (nested block placements).
    foreach (var defProxy in model.Definitions)
    {
      int defK = pipeline.AddDefinition(defProxy.applicationId.NotNull(), defProxy.name);
      int o = 0;
      foreach (var memberId in defProxy.objects)
      {
        if (instanceKByObjectId.TryGetValue(memberId, out var instK))
        {
          pipeline.DefinesInstance(defK, instK, o++);
        }
        else if (geometryKsByObjectId.TryGetValue(memberId, out var memberGKs))
        {
          foreach (var gK in memberGKs)
          {
            pipeline.Defines(defK, gK, o++);
          }
        }
      }
    }

    // 2) render materials → HAS_MATERIAL (geometry → material node). Material proxies list OBJECT ids.
    foreach (var materialProxy in model.Materials)
    {
      var value = materialProxy.value;
      int matK = pipeline.AddMaterial(
        materialProxy.applicationId.NotNull(),
        value.diffuse,
        value.opacity,
        value.metalness,
        value.roughness
      );
      foreach (var objectId in materialProxy.objects)
      {
        if (geometryKsByObjectId.TryGetValue(objectId, out var gKs))
        {
          foreach (var gK in gKs)
          {
            pipeline.HasMaterial(gK, matK);
          }
        }
      }
    }

    // 3) object colors → HAS_COLOR (geometry → color node). Color proxies list OBJECT ids.
    foreach (var colorProxy in model.Colors)
    {
      int colorK = pipeline.AddColor(colorProxy.value);
      foreach (var objectId in colorProxy.objects)
      {
        if (geometryKsByObjectId.TryGetValue(objectId, out var gKs))
        {
          foreach (var gK in gKs)
          {
            pipeline.HasColor(gK, colorK);
          }
        }
      }
    }
  }

  // Resolves (and interns once) the flat COLLECTION node for a layer name. AutoCAD has no nested layers.
  private static int GetOrAddLayerCollection(ObjectsArtifactPipeline pipeline, string layerName, Dictionary<string, int> cache)
  {
    if (cache.TryGetValue(layerName, out var existing))
    {
      return existing;
    }
    int collK = pipeline.AddCollection(layerName, layerName, null, "Layer");
    cache[layerName] = collK;
    return collK;
  }

  private static KeyValuePair<string, object?>[] RootScalars(string speckleType, string name, string units, string sourceType) =>
    new KeyValuePair<string, object?>[]
    {
      new("speckle_type", speckleType),
      new("name", name),
      new("units", units),
      new("type", sourceType),
    };

  // Matrix4x4 (row-major) → 16 doubles, matching SerializerV2 / Transform.ToArray order.
  private static double[] Flatten(Matrix4x4 m) =>
    new[] { m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44 };

  // ── pure-Speckle snapshot passed from the UI thread (phase 1) to the worker thread (phase 2) ──────────
  private sealed record CollectedObject(
    string ApplicationId,
    string SourceType,
    string LayerName,
    Dictionary<string, object?> Properties,
    Base Converted // InstanceProxy for block instances, otherwise the AutocadObject carrier (display meshes + SAT)
  );

  private sealed record CollectedModel(
    string Units,
    IReadOnlyList<CollectedObject> Objects,
    IReadOnlyList<RenderMaterialProxy> Materials,
    IReadOnlyList<ColorProxy> Colors,
    IReadOnlyList<InstanceDefinitionProxy> Definitions,
    IReadOnlyList<SendConversionResult> Results
  );

  private sealed record BundleResult(
    IReadOnlyDictionary<string, string> Bundle,
    string RootId,
    int ObjectCount,
    IReadOnlyList<SendConversionResult> Results
  );
}
