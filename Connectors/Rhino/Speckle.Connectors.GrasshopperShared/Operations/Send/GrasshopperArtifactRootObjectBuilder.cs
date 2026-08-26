using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Diagnostics;
using Speckle.Connectors.Common.Instances;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Converters.Common;
using Speckle.Converters.Rhino;
using Speckle.DoubleNumerics;
using Speckle.Objects;
using Speckle.Objects.Other;
using Speckle.Objects.Utils;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.Models;
using Speckle.Sdk.Pipelines;
using Speckle.Sdk.Pipelines.Progress;
using Speckle.Sdk.Pipelines.Send.Artifacts;
using Path = System.IO.Path;
using SOG = Speckle.Objects.Geometry;

namespace Speckle.Connectors.GrasshopperShared.Operations.Send;

/// <summary>
/// Speckle 4.0 send path for Grasshopper: instead of assembling a <see cref="Speckle.Sdk.Models.Collections.Collection"/>
/// graph from the wrapper tree and serializing it, this walks the same wrapper tree and drives the SDK
/// <see cref="ObjectsArtifactPipeline"/> to write the client-side artefact triple directly — <c>geometries.parquet</c>
/// (SGEO blobs + raw 3dm solid blobs), <c>eav.*.parquet</c> (properties), <c>envelope.*.parquet</c> (the collection /
/// material / instance topology graph) — then uploads via <see cref="ArtifactPipeline"/>.
/// </summary>
/// <remarks>
/// <para>The receive-side of GH is unchanged; this only replaces what happens after <c>SendOperation</c> picks the
/// artefact path (registering this builder in <c>PriorityLoader</c> is the only switch — no component/UX change).</para>
/// <para>It walks the wrapper tree directly (NOT the v1 <c>GrasshopperRootObjectBuilder.Unwrap</c> that builds a
/// <c>Collection</c> Base graph): collections → <c>AddCollection</c>/<c>InCollection</c>, data objects + geometry →
/// <c>AddGeometry</c>/<c>AddRawGeometry</c>+<c>Display</c>/<c>Solid</c>, and it reuses the existing
/// <see cref="GrasshopperColorPacker"/>/<see cref="GrasshopperMaterialPacker"/>/<see cref="GrasshopperBlockPacker"/>
/// to derive the render-material/color/instance-definition edges (HAS_MATERIAL / HAS_COLOR / DEFINES /
/// DISPLAY_INSTANCE). Geometry is unwrapped with the shared <see cref="GrasshopperSendUnwrapper"/> (same clean Speckle
/// object + raw 3dm encoding the Rhino connector produces), so the SGEO path is identical to Rhino's.</para>
/// <para><b>Threading.</b> Unlike the Rhino builder there is no UI/worker split: GH hands the builder pure-Speckle
/// wrappers (converted at component solve time) and <c>SendOperation.Send</c> already runs on GH's async worker thread,
/// so the whole build+upload runs on the worker (no UI SynchronizationContext → the sync-over-async parquet IO can't
/// deadlock). The build is still wrapped in <c>RunOnWorkerAsync</c> defensively (a no-op when already off-UI).</para>
/// </remarks>
public class GrasshopperArtifactRootObjectBuilder(
  IInstanceObjectsManager<SpeckleGeometryWrapper, List<string>> instanceObjectsManager,
  IConverterSettingsStore<RhinoConversionSettings> converterSettings,
  IThreadContext threadContext,
  IArtifactPipelineFactory artifactPipelineFactory,
  ISpeckleApplication speckleApplication
) : IArtifactRootObjectBuilder<SpeckleCollectionWrapperGoo>
{
  public async Task<ArtifactBuildResult> BuildAndUpload(
    IReadOnlyList<SpeckleCollectionWrapperGoo> objects,
    string projectId,
    string ingestionId,
    string versionId,
    Account account,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    var outputDir = Path.Combine(Path.GetTempPath(), "Speckle", "artifacts", versionId);
    Directory.CreateDirectory(outputDir);

    using var session = ArtefactSessionLog.Start("Grasshopper", ArtefactDirection.Send, projectId, null, versionId);

    return await threadContext.RunOnWorkerAsync(async () =>
    {
      BundleResult built;
      using (session.Phase("Write"))
      {
        built = WriteBundle(objects, session, versionId, outputDir, onOperationProgressed, cancellationToken);
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

  [SuppressMessage("Maintainability", "CA1506:Avoid excessive class coupling")]
  private BundleResult WriteBundle(
    IReadOnlyList<SpeckleCollectionWrapperGoo> objects,
    ArtefactSessionLog session,
    string versionId,
    string outputDir,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    ZstdNativeLoader.Ensure(); // net48: ensure the parquet Zstd native is loaded (no-op on net8+)
    using var pipeline = new ObjectsArtifactPipeline(outputDir, versionId, producer: speckleApplication);
    var units = converterSettings.Current.SpeckleUnits;

    // Reused as-is from the v1 send path — they derive the color/material/instance proxies whose `.objects` arrays are
    // the artefact HAS_MATERIAL / HAS_COLOR / DEFINES / DISPLAY_INSTANCE edges (keyed by applicationId).
    var colorPacker = new GrasshopperColorPacker();
    var materialPacker = new GrasshopperMaterialPacker();
    var blockPacker = new GrasshopperBlockPacker(instanceObjectsManager);

    var geometryKsByAppId = new Dictionary<string, List<int>>(StringComparer.Ordinal);
    var instanceKByAppId = new Dictionary<string, int>(StringComparer.Ordinal);
    var instanceObjectKByAppId = new Dictionary<string, int>(StringComparer.Ordinal);
    var results = new List<SendConversionResult>();

    // Deep-copy so the walk (which stamps applicationIds + drives the block packer) never mutates canvas objects.
    var rootGoo = (SpeckleCollectionWrapperGoo)objects[0].Duplicate();
    var root = rootGoo.Value;
    root.Name = "Grasshopper Model";

    var ctx = new WalkContext(
      pipeline,
      units,
      colorPacker,
      materialPacker,
      blockPacker,
      geometryKsByAppId,
      instanceKByAppId,
      instanceObjectKByAppId,
      results,
      session,
      onOperationProgressed,
      cancellationToken
    );
    // Model-wide properties ride eav.model - object-less rows, so no synthetic object needed.
    if (root is SpeckleRootCollectionWrapper { Properties: { Count: > 0 } modelProps })
    {
      EmitModelProperties(pipeline, session, modelProps, null);
    }

    WalkCollection(ctx, root, null);

    EmitValueNodes(
      pipeline,
      colorPacker,
      materialPacker,
      blockPacker,
      geometryKsByAppId,
      instanceKByAppId,
      instanceObjectKByAppId
    );

    // Default scene view: the GH collection tree (IN_COLLECTION); the CONTAINER parent chain carries the nesting.
    pipeline.AddSceneView(new SceneView(0, "Default", true, new[] { SceneViewKey.Rel(RelKind.InCollection) }));

    pipeline.Complete();

    var bundle = Directory
      .EnumerateFiles(outputDir, versionId + ".*")
      .Where(p => p.EndsWith(".parquet", StringComparison.Ordinal))
      .ToDictionary(p => Path.GetFileName(p)!, p => p, StringComparer.Ordinal);

    var objectCount = results.Count(r => r.Status == Status.SUCCESS);
    var rootId = $"binary-{versionId}";

    session.SetStat("files", bundle.Count);
    session.SetStat("objects", objectCount);
    session.SetStat("materials", materialPacker.RenderMaterialProxies.Count);
    session.SetStat("definitions", blockPacker.InstanceDefinitionProxies.Count);
    return new BundleResult(bundle, rootId, objectCount, results);
  }

  // ── walk (wrapper tree → pipeline calls) ──────────────────────────────────────────────────────────────
  private void WalkCollection(WalkContext ctx, SpeckleCollectionWrapper collWrapper, int? parentCollK)
  {
    ctx.CancellationToken.ThrowIfCancellationRequested();
    collWrapper.ApplicationId ??= collWrapper.GetSpeckleApplicationId();
    // GH data-tree topology rides nodes.gh_topology, the column the spec carves out for it. It used to need a
    // synthetic object; the objects table is for real objects [ENG-9291].
    int collK = ctx.Pipeline.AddCollection(
      collWrapper.ApplicationId,
      collWrapper.Name,
      parentCollK,
      "Collection",
      ghTopology: collWrapper.Topology is { Length: > 0 } topology ? topology : null
    );

    // collection-level color/material are collected for parity with the v1 walk; they resolve to no geometry K and are
    // therefore not emitted as HAS_* edges (same as Rhino's layer-level materials).
    ctx.ColorPacker.ProcessColor(collWrapper.ApplicationId, collWrapper.Color);
    ctx.MaterialPacker.ProcessMaterial(collWrapper.ApplicationId, collWrapper.Material);

    int ord = 0;
    foreach (var element in collWrapper.Elements)
    {
      switch (element)
      {
        case null:
          continue;
        case SpeckleCollectionWrapper child:
          WalkCollection(ctx, child, collK);
          break;
        case SpeckleBlockInstanceWrapper instance: // must precede SpeckleGeometryWrapper (it is a subtype)
          EmitInstance(ctx, instance, collK, ord++);
          break;
        case SpeckleDataObjectWrapper dataObject:
          EmitDataObject(ctx, dataObject, collK, ord++);
          break;
        case SpeckleGeometryWrapper geometry:
          EmitGeometryObject(ctx, geometry, collK, ord++);
          break;
      }
    }
  }

  // A standalone geometry object (also used for block-definition member geometry): object = its own display geometry.
  // Definition members render ONLY through their definition (DEFINES); pass isDefinitionMember to suppress the
  // standalone top-level edges (IN_COLLECTION / DISPLAY / SOLID) while still registering the geometry K for DEFINES.
  private void EmitGeometryObject(
    WalkContext ctx,
    SpeckleGeometryWrapper wrapper,
    int collK,
    int ord,
    bool isDefinitionMember = false
  )
  {
    var sw = Stopwatch.StartNew();
    string sourceType = wrapper.Base.speckle_type;
    try
    {
      Base clean = GrasshopperSendUnwrapper.UnwrapGeometry(wrapper);
      string appId = clean.applicationId.NotNull();
      int objK = ctx.Pipeline.InternObject(appId);
      if (!isDefinitionMember)
      {
        ctx.Pipeline.InCollection(objK, collK, ord);
      }
      ctx.Pipeline.AddProperties(
        appId,
        PropertiesOf(clean),
        RootScalars(clean.speckle_type, wrapper.Name, ctx.Units, sourceType)
      );

      int displayOrd = 0;
      int solidOrd = 0;
      EmitGeometryFragments(ctx, objK, appId, clean, ref displayOrd, ref solidOrd, isDefinitionMember);

      ctx.ColorPacker.ProcessColor(appId, wrapper.Color);
      ctx.MaterialPacker.ProcessMaterial(appId, wrapper.Material);

      ctx.Results.Add(new(Status.SUCCESS, appId, sourceType, clean));
      ctx.Session.RecordObject(appId, sourceType, Status.SUCCESS, null, sw.ElapsedMilliseconds);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      ctx.Results.Add(new(Status.ERROR, wrapper.ApplicationId ?? "?", sourceType, null, ex));
      ctx.Session.RecordObject(
        wrapper.ApplicationId ?? "?",
        sourceType,
        Status.ERROR,
        ex.Message,
        sw.ElapsedMilliseconds
      );
    }
  }

  // A data object: one interned object whose display geometry are its child geometry wrappers.
  private void EmitDataObject(WalkContext ctx, SpeckleDataObjectWrapper wrapper, int collK, int ord)
  {
    var sw = Stopwatch.StartNew();
    string appId = wrapper.DataObject.applicationId ?? wrapper.ApplicationId ?? Guid.NewGuid().ToString();
    string sourceType = wrapper.DataObject.speckle_type;
    try
    {
      int objK = ctx.Pipeline.InternObject(appId);
      ctx.Pipeline.InCollection(objK, collK, ord);
      ctx.Pipeline.AddProperties(
        appId,
        wrapper.DataObject.properties ?? s_emptyProps,
        RootScalars(wrapper.DataObject.speckle_type, wrapper.Name, ctx.Units, sourceType)
      );

      int displayOrd = 0;
      int solidOrd = 0;
      foreach (var geometryWrapper in wrapper.Geometries)
      {
        Base clean = GrasshopperSendUnwrapper.UnwrapGeometry(geometryWrapper);
        string geometryAppId = geometryWrapper.ApplicationId ?? clean.applicationId ?? $"{appId}:g";
        EmitGeometryFragments(ctx, objK, geometryAppId, clean, ref displayOrd, ref solidOrd);
        ctx.ColorPacker.ProcessColor(geometryAppId, geometryWrapper.Color);
        ctx.MaterialPacker.ProcessMaterial(geometryAppId, geometryWrapper.Material);
      }

      ctx.Results.Add(new(Status.SUCCESS, appId, sourceType, wrapper.DataObject));
      ctx.Session.RecordObject(appId, sourceType, Status.SUCCESS, null, sw.ElapsedMilliseconds);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      ctx.Results.Add(new(Status.ERROR, appId, sourceType, null, ex));
      ctx.Session.RecordObject(appId, sourceType, Status.ERROR, ex.Message, sw.ElapsedMilliseconds);
    }
  }

  // Block instance: registers (this + nested definitions) via the block packer, emits the instance node(s), and emits
  // the definition member geometry so DEFINES/DEFINES_INSTANCE can resolve in EmitValueNodes.
  private void EmitInstance(WalkContext ctx, SpeckleBlockInstanceWrapper instance, int collK, int ord)
  {
    var sw = Stopwatch.StartNew();
    var definitionObjects = ctx.BlockPacker.ProcessInstance(instance);
    string appId = instance.ApplicationId ?? Guid.NewGuid().ToString();
    const string SOURCE_TYPE = "Instance (Block)";
    try
    {
      EmitInstanceNode(ctx, instance, collK, ord);

      if (definitionObjects != null)
      {
        foreach (var definitionObject in definitionObjects)
        {
          // nested instances are already registered by ProcessInstance above → emit their node only.
          if (definitionObject is SpeckleBlockInstanceWrapper nested)
          {
            EmitInstanceNode(ctx, nested, collK, ord, isNested: true);
          }
          else
          {
            // A block-definition member renders ONLY through its definition (DEFINES, via a placed instance's
            // transform) — emit its geometry K so DEFINES resolves, but NO top-level DISPLAY / SOLID / IN_COLLECTION.
            EmitGeometryObject(ctx, definitionObject, collK, ord, isDefinitionMember: true);
          }
        }
      }

      ctx.Results.Add(new(Status.SUCCESS, appId, SOURCE_TYPE, instance.InstanceProxy));
      ctx.Session.RecordObject(appId, SOURCE_TYPE, Status.SUCCESS, null, sw.ElapsedMilliseconds);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      ctx.Results.Add(new(Status.ERROR, appId, SOURCE_TYPE, null, ex));
      ctx.Session.RecordObject(appId, SOURCE_TYPE, Status.ERROR, ex.Message, sw.ElapsedMilliseconds);
    }
  }

  // Emits pipeline calls for an already-registered instance (object → INSTANCE node via DISPLAY_INSTANCE).
  //
  // A NESTED instance is a member of its parent definition and reaches the scene only through that definition's
  // DEFINES_INSTANCE, applied under the parent placement's transform. Giving it IN_COLLECTION and its own
  // DISPLAY_INSTANCE would place it a second time, at its definition-local transform: Grasshopper filters that
  // duplicate out on receive, but the Viewer, Rhino and AutoCAD do not, so they draw it twice - once in place and
  // once adrift near the origin [ENG-9161]. The INSTANCE node itself is still emitted, since DEFINES_INSTANCE
  // needs something to point at.
  private void EmitInstanceNode(
    WalkContext ctx,
    SpeckleBlockInstanceWrapper instance,
    int collK,
    int ord,
    bool isNested = false
  )
  {
    string appId = instance.ApplicationId.NotNull();
    if (ctx.InstanceKByAppId.ContainsKey(appId))
    {
      return; // a shared nested definition can be reached twice; place it once
    }
    int objK = ctx.Pipeline.InternObject(appId);
    if (!isNested)
    {
      ctx.Pipeline.InCollection(objK, collK, ord);
    }
    ctx.Pipeline.AddProperties(
      appId,
      PropertiesOf(instance.InstanceProxy),
      RootScalars(instance.InstanceProxy.speckle_type, instance.Name, ctx.Units, "Instance (Block)")
    );
    int defK = ctx.Pipeline.AddDefinition(instance.InstanceProxy.definitionId, null);
    int instK = ctx.Pipeline.AddInstance(
      appId,
      defK,
      Flatten(instance.InstanceProxy.transform),
      instance.InstanceProxy.units
    );
    if (!isNested)
    {
      ctx.Pipeline.DisplayInstance(objK, instK, 0);
    }

    // A placement's own colour and material hang off its OBJECT K, not any geometry — EmitValueNodes turns these
    // into OBJECT_HAS_COLOR / OBJECT_HAS_MATERIAL [bundle-spec rels 27/26] [ENG-9163].
    ctx.ColorPacker.ProcessColor(appId, instance.Color);
    ctx.MaterialPacker.ProcessMaterial(appId, instance.Material);

    ctx.InstanceKByAppId[appId] = instK;
    ctx.InstanceObjectKByAppId[appId] = objK;
  }

  // Splits a clean Speckle object into its lossless raw 3dm SOLID blob (if any) + DISPLAY geometry; records the display
  // geometry K(s) under geometryAppId so HAS_MATERIAL/HAS_COLOR/DEFINES resolve. Mirrors the Rhino artefact builder.
  private static void EmitGeometryFragments(
    WalkContext ctx,
    int objK,
    string geometryAppId,
    Base clean,
    ref int displayOrd,
    ref int solidOrd,
    bool isDefinitionMember = false
  )
  {
    List<Base> displayGeometry;
    RawEncoding? rawEncoding = null;
    if (clean is IDisplayValue<List<SOG.Mesh>> hasDisplay && clean is SOG.IRawEncodedObject rawEncoded)
    {
      displayGeometry = hasDisplay.displayValue.Cast<Base>().ToList();
      rawEncoding = rawEncoded.encodedValue;
    }
    else if (clean is IDisplayValue<List<SOG.Mesh>> hasDisplayMeshes)
    {
      displayGeometry = hasDisplayMeshes.displayValue.Cast<Base>().ToList();
    }
    else
    {
      displayGeometry = [clean];
    }

    // The authoritative 3dm blob, kept verbatim so a closed Brep, Extrusion or SubD comes back as itself rather than
    // as its display mesh. A standalone object links it with a SOLID edge; a definition member has no standalone
    // placement, so its solid rides DEFINES instead - added to gKs below, alongside the display meshes, and the
    // receiver prefers it per member [ENG-9160]. Mirrors RhinoBundleBuilder.
    int? memberSolidK = null;
    if (rawEncoding is not null && rawEncoding.format == RawEncodingFormats.RHINO_3DM)
    {
      byte[] solidBytes = Convert.FromBase64String(rawEncoding.contents);
      int solidK = ctx.Pipeline.AddRawGeometry($"{geometryAppId}:solid", solidBytes, RawEncodingFormats.RHINO_3DM);
      if (isDefinitionMember)
      {
        memberSolidK = solidK;
      }
      else
      {
        ctx.Pipeline.Solid(objK, solidK, solidOrd++);
      }
    }

    var gKs = ctx.GeometryKsByAppId.TryGetValue(geometryAppId, out var existing)
      ? existing
      : ctx.GeometryKsByAppId[geometryAppId] = new List<int>();

    if (memberSolidK is int msk)
    {
      gKs.Add(msk);
    }

    int fragOrd = 0;
    foreach (Base fragment in displayGeometry)
    {
      string gAppId = fragment.applicationId ?? $"{geometryAppId}:g{fragOrd++}";
      int gK = ctx.Pipeline.AddGeometry(gAppId, fragment);
      if (!isDefinitionMember)
      {
        ctx.Pipeline.Display(objK, gK, displayOrd++); // members render only via DEFINES through a placed instance's transform
      }
      gKs.Add(gK);
    }
  }

  // ── value nodes (definitions / materials / colors) — after the walk so referenced geometry/instances exist ──
  private static void EmitValueNodes(
    ObjectsArtifactPipeline pipeline,
    GrasshopperColorPacker colorPacker,
    GrasshopperMaterialPacker materialPacker,
    GrasshopperBlockPacker blockPacker,
    Dictionary<string, List<int>> geometryKsByAppId,
    Dictionary<string, int> instanceKByAppId,
    Dictionary<string, int> instanceObjectKByAppId
  )
  {
    foreach (var defProxy in blockPacker.InstanceDefinitionProxies.Values)
    {
      int defK = pipeline.AddDefinition(defProxy.applicationId.NotNull(), defProxy.name);
      int memberOrd = 0;
      foreach (var memberId in defProxy.objects)
      {
        if (instanceKByAppId.TryGetValue(memberId, out var instK))
        {
          pipeline.DefinesInstance(defK, instK, memberOrd);
        }
        else if (geometryKsByAppId.TryGetValue(memberId, out var memberGKs))
        {
          // All geometry of one member shares its member ordinal, so receive can group the member's authoritative solid
          // + its display mesh(es) and pick the solid over its shadow (mirrors RhinoBundleBuilder).
          foreach (var gK in memberGKs)
          {
            pipeline.Defines(defK, gK, memberOrd);
          }
        }
        memberOrd++;
      }
    }

    foreach (var materialProxy in materialPacker.RenderMaterialProxies.Values)
    {
      var value = materialProxy.value;
      int matK = pipeline.AddMaterial(
        materialProxy.applicationId.NotNull(),
        value.name,
        value.diffuse,
        value.opacity,
        value.metalness,
        value.roughness,
        value.emissive,
        value["ior"] as double? // dynamic prop (v1 unpacker convention); null when the host has no IOR [ENG-8791]
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
        else if (instanceKByAppId.ContainsKey(objectId))
        {
          // a placement paints its own material: no geometry of its own, so it lives on the object plane as
          // OBJECT_HAS_MATERIAL [bundle-spec rel 26] — the retired HAS_MATERIAL ord=1 INSTANCE-src overload is gone.
          pipeline.ObjectHasMaterial(pipeline.InternObject(objectId), matK);
        }
      }
    }

    foreach (var colorProxy in colorPacker.ColorProxies.Values)
    {
      int colorK = pipeline.AddColor(colorProxy.value);
      foreach (var objectId in colorProxy.objects)
      {
        if (geometryKsByAppId.TryGetValue(objectId, out var gKs))
        {
          foreach (var gK in gKs)
          {
            pipeline.HasColor(gK, colorK);
          }
        }
        else if (instanceObjectKByAppId.TryGetValue(objectId, out var objK))
        {
          // a placement paints its own colour: no geometry of its own, so it lives on the object plane as
          // OBJECT_HAS_COLOR [bundle-spec rel 27] — the retired HAS_COLOR ord=1 object-src overload is gone.
          pipeline.ObjectHasColor(objK, colorK);
        }
      }
    }
  }

  /// <summary>
  /// Flattens the nested dict into the dotted paths eav.model expects, one row per leaf. Same delimiter as
  /// SpecklePropertyGroupGoo, so the reader nests it straight back.
  /// </summary>
  private static void EmitModelProperties(
    ObjectsArtifactPipeline pipeline,
    ArtefactSessionLog session,
    IReadOnlyDictionary<string, object?> props,
    string? prefix
  )
  {
    foreach (var kv in props)
    {
      string path = prefix is null ? kv.Key : $"{prefix}{Constants.PROPERTY_PATH_DELIMITER}{kv.Key}";
      switch (kv.Value)
      {
        case IReadOnlyDictionary<string, object?> nested:
          EmitModelProperties(pipeline, session, nested, path);
          break;
        // a converted Plane or Vector is the one leaf eav.model can't hold - stringifying it stores a type name
        case Base:
          session.Increment("modelPropertiesSkipped");
          break;
        default:
          pipeline.AddModelProperty(path, kv.Value); // drops nulls and non-finite numerics itself
          break;
      }
    }
  }

  private static readonly Dictionary<string, object?> s_emptyProps = new();

  private static IReadOnlyDictionary<string, object?> PropertiesOf(Base @base) =>
    @base["properties"] is IReadOnlyDictionary<string, object?> props ? props : s_emptyProps;

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

  private static double[] Flatten(Matrix4x4 m) =>
    new[]
    {
      m.M11,
      m.M12,
      m.M13,
      m.M14,
      m.M21,
      m.M22,
      m.M23,
      m.M24,
      m.M31,
      m.M32,
      m.M33,
      m.M34,
      m.M41,
      m.M42,
      m.M43,
      m.M44,
    };

  // Threaded through the recursive walk instead of a field-per-build, so the builder stays reentrant-safe.
  private sealed record WalkContext(
    ObjectsArtifactPipeline Pipeline,
    string Units,
    GrasshopperColorPacker ColorPacker,
    GrasshopperMaterialPacker MaterialPacker,
    GrasshopperBlockPacker BlockPacker,
    Dictionary<string, List<int>> GeometryKsByAppId,
    Dictionary<string, int> InstanceKByAppId,
    // a placement's colour tags on its OBJECT and its material on its INSTANCE node, so both Ks are needed
    Dictionary<string, int> InstanceObjectKByAppId,
    List<SendConversionResult> Results,
    ArtefactSessionLog Session,
    IProgress<CardProgress> Progress,
    CancellationToken CancellationToken
  );

  private sealed record BundleResult(
    IReadOnlyDictionary<string, string> Bundle,
    string RootId,
    int ObjectCount,
    IReadOnlyList<SendConversionResult> Results
  );
}
