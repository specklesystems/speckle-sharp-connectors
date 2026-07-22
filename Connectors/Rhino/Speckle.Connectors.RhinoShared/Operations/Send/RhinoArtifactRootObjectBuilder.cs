using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Microsoft.Extensions.Logging;
using Rhino.DocObjects;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Diagnostics;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.Rhino.HostApp;
using Speckle.Connectors.Rhino.HostApp.Properties;
using Speckle.Converters.Common;
using Speckle.Converters.Rhino;
using Speckle.Converters.Rhino.ToSpeckle.Encoding;
using Speckle.DoubleNumerics;
using Speckle.Objects;
using Speckle.Objects.Other;
using Speckle.Objects.Utils;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Instances;
using Speckle.Sdk.Models.Proxies;
using Speckle.Sdk.Pipelines;
using Speckle.Sdk.Pipelines.Progress;
using Speckle.Sdk.Pipelines.Send.Artifacts;
using RG = Rhino.Geometry;
using RhinoLayer = Rhino.DocObjects.Layer;
using SOG = Speckle.Objects.Geometry;

namespace Speckle.Connectors.Rhino.Operations.Send;

/// <summary>
/// Speckle 4.0 send path for Rhino: instead of building a <see cref="Speckle.Sdk.Models.Collections.Collection"/>
/// graph of <see cref="Speckle.Objects.Data.RhinoObject"/>s and serializing it through the v1 pipeline, this
/// drives the SDK <see cref="ObjectsArtifactPipeline"/> to write the client-side artefact triple directly —
/// <c>geometries.parquet</c> (SGEO blobs + raw 3dm solid blobs), <c>eav.*.parquet</c> (properties),
/// <c>envelope.*.parquet</c> (the relations + value-node topology graph) — then uploads the bundle via
/// <see cref="ArtifactPipeline"/>.
/// </summary>
/// <remarks>
/// <para>Mirrors <c>RhinoRootObjectBuilder</c>'s convert/unpack flow (instances, materials, properties,
/// display meshing) but emits into the artefact pipeline. Two Rhino-specific shapes:</para>
/// <list type="number">
///   <item><b>Solids.</b> A Brep/Extrusion/SubD is kept losslessly as the raw 3dm blob the converter already
///   produced (<c>RhinoObject.rawEncoding</c>, base64 3dm). It is written to <c>geometries.parquet</c> verbatim
///   with <c>type = "3dm"</c> and linked via the <c>SOLID</c> rel (for receive-as-solids), <b>alongside</b> its
///   display meshes which link via the <c>DISPLAY</c> rel (for the viewer).</item>
///   <item><b>Layers are the default scene view.</b> The Rhino layer tree becomes <c>COLLECTION</c> nodes
///   (nested via <c>ParentLayerId</c>) with an <c>IN_COLLECTION</c> rel per object, and the default scene view
///   projects on <c>IN_COLLECTION</c> — so the viewer explorer reproduces the layer hierarchy.</item>
/// </list>
/// <para><b>Threading.</b> Two phases. Phase 1 (<see cref="CollectOnMain"/>) runs on the Rhino UI thread —
/// RhinoCommon (convert, unpack, layer/attribute reads) is main-thread-affine — and produces a pure-Speckle
/// snapshot (no RhinoCommon refs). Phase 2 (<see cref="WriteBundle"/>) runs on a worker thread and builds the
/// parquet bundle. This split is mandatory: the artefact pipeline does sync-over-async parquet IO
/// (<c>ParquetWriter.CreateAsync(...).GetAwaiter().GetResult()</c>), which DEADLOCKS on the UI thread (the
/// continuation is posted back to the blocked UI dispatcher/scheduler). Running it on a worker — no UI
/// SynchronizationContext or TaskScheduler — lets those continuations resume on the thread pool. Same principle
/// as speckle-oda, which runs the whole extraction on a dedicated pinned thread off the host UI thread.</para>
/// </remarks>
[SuppressMessage(
  "Maintainability",
  "CA1506:Avoid excessive class coupling",
  Justification = "Top-level artefact send orchestrator; coupling to converters, unpackers, host API and the pipeline façade is inherent."
)]
public class RhinoArtifactRootObjectBuilder(
  IRootToSpeckleConverter converter,
  IConverterSettingsStore<RhinoConversionSettings> converterSettings,
  RhinoInstanceUnpacker instanceUnpacker,
  RhinoMaterialUnpacker materialUnpacker,
  RhinoColorUnpacker colorUnpacker,
  PropertiesExtractor propertiesExtractor,
  IThreadContext threadContext,
  IArtifactPipelineFactory artifactPipelineFactory,
  ILogger<RhinoArtifactRootObjectBuilder> logger
) : IArtifactRootObjectBuilder<RhinoObject>
{
  public async Task<ArtifactBuildResult> BuildAndUpload(
    IReadOnlyList<RhinoObject> objects,
    string projectId,
    string ingestionId,
    string versionId,
    Account account,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    // Bundle base name = the server pre-allocated versionId, so the parquet files carry their final names
    // from byte one (the v2 upload signs/keys per basename). Each version gets its own scratch dir.
    var outputDir = Path.Combine(Path.GetTempPath(), "Speckle", "artifacts", versionId);
    Directory.CreateDirectory(outputDir);

    // Per-session diagnostics (per-object timing/failures, phase timings, bundle stats) → %TEMP%\Speckle\sessions\.
    using var session = ArtefactSessionLog.Start("Rhino", ArtefactDirection.Send, projectId, null, versionId, logger);

    // Phase 1 — convert + unpack on the Rhino UI thread (RhinoCommon is main-thread-affine) → pure-Speckle snapshot.
    CollectedModel collected;
    using (session.Phase("Collect"))
    {
      collected = await threadContext.RunOnMainAsync(() =>
        Task.FromResult(CollectOnMain(objects, session, onOperationProgressed, cancellationToken))
      );
    }

    // Phase 2 — build the parquet bundle + upload on a WORKER thread (no UI SynchronizationContext/TaskScheduler,
    // so the pipeline's sync-over-async parquet IO doesn't deadlock — see the class <remarks>).
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

  // ── Phase 1 (UI thread): RhinoCommon → pure-Speckle snapshot ─────────────────────────────────────────
  private CollectedModel CollectOnMain(
    IReadOnlyList<RhinoObject> rhinoObjects,
    ArtefactSessionLog session,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    var doc = converterSettings.Current.Document;
    var units = converterSettings.Current.SpeckleUnits;

    // Unpack instances → atomic objects (incl. block-definition members) + instance/definition proxies.
    var (atomicObjects, _, instanceProxies, instanceDefinitionProxies) = instanceUnpacker.UnpackSelection(rhinoObjects);

    var layers = new Dictionary<int, CollectedLayer>();
    var usedLayers = new List<RhinoLayer>();
    var collectedObjects = new List<CollectedObject>(atomicObjects.Count);
    var results = new List<SendConversionResult>(atomicObjects.Count);

    int count = 0;
    foreach (RhinoObject rhinoObject in atomicObjects)
    {
      cancellationToken.ThrowIfCancellationRequested();
      string applicationId = rhinoObject.Id.ToString();
      string sourceType = rhinoObject.ObjectType.ToString();
      var sw = Stopwatch.StartNew();
      try
      {
        int layerIndex = rhinoObject.Attributes.LayerIndex;
        CollectLayerChain(doc, layerIndex, layers, usedLayers);

        CollectedObject collected = CollectObject(
          rhinoObject,
          applicationId,
          sourceType,
          layerIndex,
          units,
          instanceProxies
        );
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

    // Authored scene groups → plain snapshot records (Base-free — no GroupProxy). Membership lists OBJECT ids.
    var groups = CollectGroups(doc, atomicObjects);

    // Render materials are keyed by material name; proxies list OBJECT ids (resolved to geometry K(s) in phase 2).
    var materials = materialUnpacker.UnpackRenderMaterials(atomicObjects, usedLayers);

    // By-object display colours → COLOR nodes + HAS_COLOR. Layer colours ride the layer collection nodes' argb, so pass
    // no layers here (object-level only) to avoid double-emitting them.
    var colors = colorUnpacker.UnpackColors(atomicObjects, new List<RhinoLayer>());

    // Named views → camera_views rows (Base-free). Unlike the v1 Camera path, parallel projections carry over too.
    var cameraViews = CollectNamedViews(doc, units);

    return new CollectedModel(
      units,
      collectedObjects,
      layers,
      materials,
      colors,
      instanceDefinitionProxies,
      groups,
      cameraViews,
      results
    );
  }

  // Rhino groups → plain snapshot records. An object's GetGroupList carries ALL its groups (nesting in Rhino is
  // implicit via overlapping membership — there is no group parent chain), so each entry becomes one IN_GROUP edge.
  // RhinoCommon-bound (GroupTable) → phase 1 only.
  private List<CollectedGroup> CollectGroups(global::Rhino.RhinoDoc doc, IReadOnlyList<RhinoObject> atomicObjects)
  {
    var groups = new Dictionary<string, CollectedGroup>(StringComparer.Ordinal);
    foreach (RhinoObject rhinoObject in atomicObjects)
    {
      try
      {
        int[]? groupList = rhinoObject.GetGroupList();
        if (groupList is null)
        {
          continue;
        }
        foreach (int groupIndex in groupList)
        {
          Group? group = doc.Groups.FindIndex(groupIndex);
          if (group is null)
          {
            continue;
          }
          string groupId = group.Id.ToString();
          if (!groups.TryGetValue(groupId, out CollectedGroup? collected))
          {
            collected = new CollectedGroup(groupId, group.Name, new List<string>());
            groups[groupId] = collected;
          }
          collected.MemberIds.Add(rhinoObject.Id.ToString());
        }
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        logger.LogWarning(ex, "Failed to unpack groups for {AppId}", rhinoObject.Id);
      }
    }
    return groups.Values.ToList();
  }

  // Rhino named views → envelope camera_views. RhinoCommon-bound (NamedViewTable) → phase 1 only. Positions/target
  // are in doc model units; forward/up are unitized. Perspective views carry lens_mm + a frustum-derived vertical
  // fov (degrees); parallel views carry ortho_height (the near-frustum height) instead.
  private static List<CameraView> CollectNamedViews(global::Rhino.RhinoDoc doc, string units)
  {
    var views = new List<CameraView>();
    int ord = 0;
    foreach (ViewInfo namedView in doc.NamedViews)
    {
      var vp = namedView.Viewport;
      var forward = vp.CameraDirection;
      var up = vp.CameraUp;
      if (!forward.Unitize() || !up.Unitize())
      {
        continue; // degenerate camera — skip rather than fail the send
      }

      bool isOrtho = vp.IsParallelProjection;
      bool hasFrustum = vp.GetFrustum(
        out double left,
        out double right,
        out double bottom,
        out double top,
        out double near,
        out double far
      );
      _ = left;
      _ = right;
      double? fov = null;
      if (!isOrtho && hasFrustum && near > 0)
      {
        fov = 2.0 * Math.Atan2((top - bottom) / 2.0, near) * (180.0 / Math.PI);
      }

      bool hasTarget = vp.TargetPoint.IsValid;
      views.Add(
        new CameraView(
          View: ord,
          Name: namedView.Name,
          IsDefault: false,
          Ord: ord,
          PosX: vp.CameraLocation.X,
          PosY: vp.CameraLocation.Y,
          PosZ: vp.CameraLocation.Z,
          ForwardX: forward.X,
          ForwardY: forward.Y,
          ForwardZ: forward.Z,
          UpX: up.X,
          UpY: up.Y,
          UpZ: up.Z,
          TargetX: hasTarget ? vp.TargetPoint.X : null,
          TargetY: hasTarget ? vp.TargetPoint.Y : null,
          TargetZ: hasTarget ? vp.TargetPoint.Z : null,
          Units: units,
          IsOrtho: isOrtho,
          Fov: fov,
          LensMm: isOrtho ? null : vp.Camera35mmLensLength,
          OrthoHeight: isOrtho && hasFrustum ? top - bottom : null,
          Aspect: vp.FrustumAspect,
          Near: hasFrustum ? near : null,
          Far: hasFrustum ? far : null
        )
      );
      ord++;
    }
    return views;
  }

  // Converts one Rhino object to its Speckle representation (instance proxy or geometry) + extracts properties.
  // RhinoCommon-bound (converter.Convert, attribute reads) → phase 1 only. The display/encoding split is left to
  // phase 2 (it's pure Speckle, no RhinoCommon).
  private CollectedObject CollectObject(
    RhinoObject rhinoObject,
    string applicationId,
    string sourceType,
    int layerIndex,
    string units,
    IReadOnlyDictionary<string, InstanceProxy> instanceProxies
  )
  {
    string name = !string.IsNullOrEmpty(rhinoObject.Attributes.Name) ? rhinoObject.Attributes.Name : sourceType;

    if (rhinoObject is InstanceObject)
    {
      InstanceProxy instanceProxy = instanceProxies[applicationId];
      var instanceProps = propertiesExtractor.GetProperties(rhinoObject);
      return new CollectedObject(applicationId, name, sourceType, layerIndex, instanceProps, instanceProxy);
    }

    Base rawGeometry = converter.Convert(rhinoObject);
    var properties = propertiesExtractor.GetProperties(rhinoObject);
    propertiesExtractor.AddGeometryProperties(properties, rawGeometry, units);
    RawEncoding? rawSolid = null;
    if (rhinoObject.Geometry is RG.Hatch hatch)
    {
      // A hatch converts to a display-only Region (no IRawEncodedObject). Serialize the native hatch to a 3dm blob here
      // (main thread) so it can be sent as the authoritative SOLID for a lossless Rhino→Rhino receive; the pattern
      // styling still rides eav for the viewer/other receivers.
      AddHatchProperties(properties, hatch);
      rawSolid = RawEncodingCreator.Encode(hatch, converterSettings.Current.Document);
    }
    return new CollectedObject(applicationId, name, sourceType, layerIndex, properties, rawGeometry, rawSolid);
  }

  // Hatch styling → EAV so receive can rebuild the pattern. The SGEO Region blob carries only the boundary geometry;
  // the pattern name/rotation/scale are per-object attributes and ride the eav properties, resolved by name on receive.
  private void AddHatchProperties(Dictionary<string, object?> properties, RG.Hatch hatch)
  {
    var patterns = converterSettings.Current.Document.HatchPatterns;
    if (hatch.PatternIndex >= 0 && hatch.PatternIndex < patterns.Count)
    {
      properties["hatchPatternName"] = patterns[hatch.PatternIndex].Name;
    }
    properties["hatchRotation"] = hatch.PatternRotation;
    properties["hatchScale"] = hatch.PatternScale;
  }

  // Walks a layer + its ancestors (via ParentLayerId) into the snapshot, keyed by layer index. RhinoCommon-bound.
  private static void CollectLayerChain(
    global::Rhino.RhinoDoc doc,
    int layerIndex,
    Dictionary<int, CollectedLayer> layers,
    List<RhinoLayer> usedLayers
  )
  {
    while (layerIndex >= 0 && !layers.ContainsKey(layerIndex))
    {
      RhinoLayer layer = doc.Layers[layerIndex];
      int? parentIndex = null;
      if (layer.ParentLayerId != Guid.Empty)
      {
        var parent = doc.Layers.FindId(layer.ParentLayerId);
        if (parent != null)
        {
          parentIndex = parent.Index;
        }
      }
      layers[layerIndex] = new CollectedLayer(layer.Id.ToString(), layer.Name, parentIndex, layer.Color.ToArgb());
      usedLayers.Add(layer);
      layerIndex = parentIndex ?? -1;
    }
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
    ZstdNativeLoader.Ensure(logger); // net48: ensure the parquet Zstd native is loaded (no-op on net8+)
    using var pipeline = new ObjectsArtifactPipeline(outputDir, versionId);

    // Pre-create DEFINITION nodes so they carry their proper name (the per-object pass only has the definitionId).
    foreach (var defProxy in model.Definitions)
    {
      pipeline.AddDefinition(defProxy.applicationId.NotNull(), defProxy.name);
    }

    var layerCollectionKByIndex = new Dictionary<int, int>();
    // object id -> its display-mesh geometry K(s): used post-loop for HAS_MATERIAL and DEFINES.
    var geometryKsByObjectId = new Dictionary<string, List<int>>(StringComparer.Ordinal);
    // object id -> its INSTANCE node K, for DEFINES_INSTANCE (nested block placements).
    var instanceKByObjectId = new Dictionary<string, int>(StringComparer.Ordinal);
    // Block-definition members (geometry + nested instances) are unpacked into model.Objects too, but they must
    // render ONLY through their definition (DEFINES / DEFINES_INSTANCE) via a placed instance's transform — never as
    // standalone scene objects. Without suppressing their top-level DISPLAY / DISPLAY_INSTANCE + IN_COLLECTION they
    // also draw at the model origin (untransformed), duplicating the instance geometry [ENG-8782].
    var definitionMemberIds = model.Definitions.SelectMany(d => d.objects).ToHashSet(StringComparer.Ordinal);

    int count = 0;
    foreach (CollectedObject co in model.Objects)
    {
      cancellationToken.ThrowIfCancellationRequested();
      int collK = GetOrAddLayerCollection(pipeline, model.Layers, co.LayerIndex, layerCollectionKByIndex);
      EmitObject(pipeline, co, collK, model.Units, geometryKsByObjectId, instanceKByObjectId, definitionMemberIds);
      onOperationProgressed.Report(new("Building", (double)++count / model.Objects.Count));
    }

    EmitValueNodes(pipeline, model, geometryKsByObjectId, instanceKByObjectId);
    EmitGroups(pipeline, model.Groups, definitionMemberIds);

    // Default scene view: the Rhino layer tree (IN_COLLECTION). The COLLECTION nodes' parent chain carries the
    // nesting, so a single projection key rebuilds the full explorer hierarchy.
    pipeline.AddSceneView(new SceneView(0, "Default", true, new[] { SceneViewKey.Rel(RelKind.InCollection) }));

    // Named camera viewpoints (collected on the UI thread in phase 1) → envelope.camera_views.parquet.
    foreach (var cameraView in model.CameraViews)
    {
      pipeline.AddCameraView(cameraView);
    }

    pipeline.Complete();

    var bundle = Directory
      .EnumerateFiles(outputDir, versionId + ".*")
      .Where(p => p.EndsWith(".parquet", StringComparison.Ordinal))
      .ToDictionary(p => Path.GetFileName(p)!, p => p, StringComparer.Ordinal);

    var objectCount = model.Results.Count(r => r.Status == Status.SUCCESS);
    // The artefact path has no serialized root object — a synthetic, deterministic root id (same convention as
    // the Revit artefact builder + the server's "synthetic root" expectation).
    var rootId = $"binary-{versionId}";

    session.SetStat("files", bundle.Count);
    session.SetStat("objects", objectCount);
    session.SetStat("definitions", model.Definitions.Count);
    session.SetStat("materials", model.Materials.Count);
    session.SetStat("layers", model.Layers.Count);
    session.SetStat("groups", model.Groups.Count);

    logger.LogInformation("Built artefact bundle: {fileCount} files, {objectCount} objects", bundle.Count, objectCount);
    return new BundleResult(bundle, rootId, objectCount, model.Results);
  }

  // Emits one object: eav labels + IN_COLLECTION, then a block placement (DISPLAY_INSTANCE → INSTANCE node) or
  // geometry — the lossless 3dm SOLID blob (if present) plus the DISPLAY meshes. Pure Speckle (no RhinoCommon).
  private void EmitObject(
    ObjectsArtifactPipeline pipeline,
    CollectedObject co,
    int collK,
    string units,
    Dictionary<string, List<int>> geometryKsByObjectId,
    Dictionary<string, int> instanceKByObjectId,
    HashSet<string> definitionMemberIds
  )
  {
    // A block-definition member renders ONLY through its definition (via a placed instance's transform), so it gets
    // NO standalone top-level render edge (DISPLAY / DISPLAY_INSTANCE) and NO scene-tree membership (IN_COLLECTION).
    // Its geometry/instance K is still registered below so DEFINES / DEFINES_INSTANCE resolve.
    bool isDefinitionMember = definitionMemberIds.Contains(co.ApplicationId);

    int objK = pipeline.InternObject(co.ApplicationId);
    if (!isDefinitionMember)
    {
      pipeline.InCollection(objK, collK, 0);
    }
    pipeline.AddProperties(
      co.ApplicationId,
      co.Properties,
      RootScalars(co.Converted.speckle_type, co.Name, units, co.SourceType)
    );

    // ── block instance: object → INSTANCE node (transform + definition) via DISPLAY_INSTANCE ──────────
    if (co.Converted is InstanceProxy instanceProxy)
    {
      int defK = pipeline.AddDefinition(instanceProxy.definitionId, null);
      int instK = pipeline.AddInstance(co.ApplicationId, defK, Flatten(instanceProxy.transform), instanceProxy.units);
      instanceKByObjectId[co.ApplicationId] = instK;
      if (!isDefinitionMember)
      {
        pipeline.DisplayInstance(objK, instK, 0); // a nested-block member places only via DEFINES_INSTANCE
      }
      return;
    }

    // ── geometry object ───────────────────────────────────────────────────────────────────────────────
    // Split display meshes from the lossless raw encoding (pure Speckle): Brep/Extrusion/SubD carry BOTH; a
    // plain Mesh/Point/Curve is its own display.
    Base rawGeometry = co.Converted;
    List<Base> displayGeometry;
    RawEncoding? rawEncoding = null;
    if (rawGeometry is IDisplayValue<List<SOG.Mesh>> hasDisplay && rawGeometry is SOG.IRawEncodedObject rawEncoded)
    {
      displayGeometry = hasDisplay.displayValue.Cast<Base>().ToList();
      rawEncoding = rawEncoded.encodedValue;
    }
    else if (rawGeometry is IDisplayValue<List<SOG.Mesh>> hasDisplayMeshes)
    {
      displayGeometry = hasDisplayMeshes.displayValue.Cast<Base>().ToList();
    }
    else
    {
      displayGeometry = [rawGeometry];
    }

    // A hatch has no IRawEncodedObject, so its Rhino-native 3dm blob (serialized in phase 1) stands in as the raw
    // encoding — it flows through the same SOLID path as Brep/Extrusion/SubD below.
    rawEncoding ??= co.RawSolid;

    // Authoritative solid: the raw 3dm blob, kept verbatim for receive-as-solids (Brep/Extrusion/SubD, and hatches).
    // A standalone object links it via the SOLID rel; a definition member instead lets it ride DEFINES (added to gKs
    // below) so the block reconstructs the native solid, not just its display mesh — but a member still gets NO
    // standalone SOLID edge (it renders only through a placed instance's transform).
    int? memberSolidK = null;
    if (rawEncoding is not null && rawEncoding.format == RawEncodingFormats.RHINO_3DM)
    {
      byte[] solidBytes = Convert.FromBase64String(rawEncoding.contents);
      int solidK = pipeline.AddRawGeometry($"{co.ApplicationId}:solid", solidBytes, RawEncodingFormats.RHINO_3DM);
      if (isDefinitionMember)
      {
        memberSolidK = solidK;
      }
      else
      {
        pipeline.Solid(objK, solidK, 0);
      }
    }

    // Renderable display meshes (and self-display primitives: points/curves the SGEO encoder supports).
    var gKs = new List<int>();
    if (memberSolidK is int msk)
    {
      gKs.Add(msk); // member's solid rides DEFINES alongside its display meshes; receive prefers the 3dm per member
    }
    int ord = 0;
    foreach (Base fragment in displayGeometry)
    {
      try
      {
        string gAppId = fragment.applicationId ?? $"{co.ApplicationId}:g{ord}";
        int gK = pipeline.AddGeometry(gAppId, fragment);
        if (!isDefinitionMember)
        {
          pipeline.Display(objK, gK, ord); // members render only via DEFINES through a placed instance's transform
        }
        gKs.Add(gK);
        ord++;
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        // A display fragment the SGEO encoder doesn't support (hatch/text/…) is skipped without failing the
        // whole object — its solid blob + properties still land.
        logger.LogWarning(
          ex,
          "Skipped unsupported display geometry {Type} on {AppId}",
          fragment.speckle_type,
          co.ApplicationId
        );
      }
    }

    geometryKsByObjectId[co.ApplicationId] = gKs;
  }

  // Definition members (DEFINES / DEFINES_INSTANCE) → render materials (HAS_MATERIAL). Order matters: all
  // referenced meshes/instances must exist (added in the object loop) before the edges that resolve them.
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
      int memberOrd = 0;
      foreach (var memberId in defProxy.objects)
      {
        if (instanceKByObjectId.TryGetValue(memberId, out var instK))
        {
          pipeline.DefinesInstance(defK, instK, memberOrd);
        }
        else if (geometryKsByObjectId.TryGetValue(memberId, out var memberGKs))
        {
          // All geometry of one member shares its member ordinal, so receive can group the member's authoritative solid
          // + its display mesh(es) and pick the solid over its shadow (see RhinoHostObjectArtefactBuilder.BuildDefinitions).
          foreach (var gK in memberGKs)
          {
            pipeline.Defines(defK, gK, memberOrd);
          }
        }
        memberOrd++;
      }
    }

    // 2) render materials → HAS_MATERIAL (geometry → material node). Rhino material proxies list OBJECT ids,
    // so resolve each object to its display-mesh geometry K(s).
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

    // 3) by-object display colors → HAS_COLOR (geometry → COLOR node). Same shape as materials: Rhino color proxies
    // list OBJECT ids, so resolve each to its display-mesh geometry K(s).
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
        else if (instanceKByObjectId.ContainsKey(objectId))
        {
          // block instance: no geometry of its own — emit the colour OBJECT-sourced (spec HAS_COLOR src is
          // geometry|object; the viewer looks up both) so per-placement overrides survive [ENG-8825].
          pipeline.HasColor(pipeline.InternObject(objectId), colorK);
        }
      }
    }
  }

  // Authored scene groups → CONTAINER("Group") nodes + IN_GROUP membership. A SEPARATE axis from IN_COLLECTION:
  // an object keeps its layer AND its group(s); memberships overlap, so an object may carry several IN_GROUP
  // edges. Definition members get no scene edges (same suppression as IN_COLLECTION), so a group emptied by
  // that is skipped entirely.
  private static void EmitGroups(
    ObjectsArtifactPipeline pipeline,
    IReadOnlyList<CollectedGroup> groups,
    HashSet<string> definitionMemberIds
  )
  {
    foreach (CollectedGroup group in groups)
    {
      var memberIds = group.MemberIds.Where(id => !definitionMemberIds.Contains(id)).ToList();
      if (memberIds.Count == 0)
      {
        continue;
      }
      int groupK = pipeline.AddContainer(group.Id, group.Name, null, "Group");
      int ord = 0;
      foreach (string memberId in memberIds)
      {
        pipeline.InGroup(pipeline.InternObject(memberId), groupK, ord++);
      }
    }
  }

  // Resolves (and interns once) the COLLECTION node for a layer index, building the ancestor chain from the
  // collected layer tree so the nesting is reproduced as nested COLLECTION nodes. Cached by layer index.
  private static int GetOrAddLayerCollection(
    ObjectsArtifactPipeline pipeline,
    IReadOnlyDictionary<int, CollectedLayer> layers,
    int layerIndex,
    Dictionary<int, int> cache
  )
  {
    if (cache.TryGetValue(layerIndex, out var existing))
    {
      return existing;
    }

    CollectedLayer layer = layers[layerIndex];
    int? parentK = null;
    if (layer.ParentIndex is int parentIndex && layers.ContainsKey(parentIndex))
    {
      parentK = GetOrAddLayerCollection(pipeline, layers, parentIndex, cache);
    }

    int collK = pipeline.AddCollection(layer.Id, layer.Name, parentK, "Layer", layer.Argb);
    cache[layerIndex] = collK;
    return collK;
  }

  private static KeyValuePair<string, object?>[] RootScalars(
    string speckleType,
    string name,
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

  // Matrix4x4 (row-major) → 16 doubles, matching SerializerV2 / Transform.ToArray order.
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

  // ── pure-Speckle snapshot passed from the UI thread (phase 1) to the worker thread (phase 2) ──────────
  private sealed record CollectedLayer(string Id, string Name, int? ParentIndex, int Argb);

  private sealed record CollectedGroup(string Id, string? Name, List<string> MemberIds);

  private sealed record CollectedObject(
    string ApplicationId,
    string Name,
    string SourceType,
    int LayerIndex,
    Dictionary<string, object?> Properties,
    Base Converted, // InstanceProxy for block instances, otherwise the converted geometry (Brep/Extrusion/SubD/Mesh/…)
    RawEncoding? RawSolid = null // a Rhino-native 3dm blob produced in phase 1 when the object has no IRawEncodedObject (hatch)
  );

  private sealed record CollectedModel(
    string Units,
    IReadOnlyList<CollectedObject> Objects,
    IReadOnlyDictionary<int, CollectedLayer> Layers,
    IReadOnlyList<RenderMaterialProxy> Materials,
    IReadOnlyList<ColorProxy> Colors,
    IReadOnlyList<InstanceDefinitionProxy> Definitions,
    IReadOnlyList<CollectedGroup> Groups,
    IReadOnlyList<CameraView> CameraViews,
    IReadOnlyList<SendConversionResult> Results
  );

  private sealed record BundleResult(
    IReadOnlyDictionary<string, string> Bundle,
    string RootId,
    int ObjectCount,
    IReadOnlyList<SendConversionResult> Results
  );
}
