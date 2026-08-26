using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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
using Speckle.Sdk.Bundles;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Instances;
using Speckle.Sdk.Models.Proxies;
using Speckle.Sdk.Pipelines.Progress;
using Speckle.Sdk.Pipelines.Send.Artifacts;
using RG = Rhino.Geometry;
using RhinoLayer = Rhino.DocObjects.Layer;
using SOG = Speckle.Objects.Geometry;

namespace Speckle.Connectors.Rhino.Operations.Send;

/// <summary>
/// Speckle 4.0 send path for Rhino: instead of building a <see cref="Speckle.Sdk.Models.Collections.Collection"/>
/// graph of <see cref="Speckle.Objects.Data.RhinoObject"/>s and serializing it through the v1 pipeline, this
/// converts into the SDK's <see cref="BundleBuilder"/>, which streams the client-side artefact bundle —
/// <c>geometries.parquet</c> (SGEO blobs + raw 3dm solid blobs), <c>eav.*.parquet</c> (properties),
/// <c>envelope.*.parquet</c> (the relations + value-node topology graph). The SDK ships it
/// (<see cref="IBundleSender"/>).
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
///   projects on <c>IN_COLLECTION</c> — so the viewer explorer reproduces the layer hierarchy. Block-definition
///   members carry the same edge (that is how their layer survives, ENG-9110) and are kept out of the tree by
///   having no render edge at all; DEFINES_MEMBER / PLACES join them back to their definition.</item>
/// </list>
/// <para><b>Threading.</b> Two phases. Phase 1 (<see cref="CollectOnMain"/>) runs on the Rhino UI thread —
/// RhinoCommon (convert, unpack, layer/attribute reads) is main-thread-affine — and produces a pure-Speckle
/// snapshot (no RhinoCommon refs). Phase 2 (<see cref="WriteBundle"/>) runs on a worker thread and writes the
/// bundle. This split is mandatory: the artefact pipeline does sync-over-async parquet IO
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
  ISpeckleApplication speckleApplication,
  ILogger<RhinoArtifactRootObjectBuilder> logger
) : IArtifactBundleBuilder<RhinoObject>
{
  /// <summary>
  /// Converts <paramref name="objects"/> into a <see cref="BundleBuilder"/> (unbuilt — the caller finishes and
  /// uploads it, or <see cref="BundleBuilder.Build"/>s it for an out-of-process upload). Collect runs on the Rhino UI
  /// thread, the bundle write on a worker — see the class remarks.
  /// </summary>
  public async Task<ArtifactBundleBuild> Build(
    IReadOnlyList<RhinoObject> objects,
    string? projectId,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    // Per-session diagnostics (per-object timing/failures, phase timings, bundle stats) → %TEMP%\Speckle\sessions\.
    // The version id is not known until the SDK creates the ingestion; the session is keyed by its start time.
    using var session = ArtefactSessionLog.Start("Rhino", ArtefactDirection.Send, projectId, null, null, logger);

    CollectedModel collected;
    using (session.Phase("Collect"))
    {
      collected = await threadContext.RunOnMainAsync(() =>
        Task.FromResult(CollectOnMain(objects, session, onOperationProgressed, cancellationToken))
      );
    }

    return await threadContext.RunOnWorkerAsync(() =>
    {
      using (session.Phase("Write"))
      {
        return Task.FromResult(WriteBundle(collected, session, onOperationProgressed, cancellationToken));
      }
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

    // An object whose material source is MaterialFromLayer gets NO object-level material proxy (the unpacker skips it);
    // the material rides the proxy its LAYER stage builds, which lists the layer id — and a layer id resolves to no
    // geometry in phase 2, so the inherited material was dropped entirely. Record object → the id of the layer whose
    // material it inherits so phase 2 can land HAS_MATERIAL on the object's own display geometry [ENG-9108].
    var layerMaterialInheritors = new Dictionary<string, string>(StringComparer.Ordinal);

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

        if (
          rhinoObject.Attributes.MaterialSource == ObjectMaterialSource.MaterialFromLayer
          && layerIndex >= 0
          && LayerHasRenderMaterial(doc.Layers[layerIndex])
        )
        {
          layerMaterialInheritors[applicationId] = doc.Layers[layerIndex].Id.ToString();
        }

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
      layerMaterialInheritors,
      results
    );
  }

  // True when the layer itself carries a render material. Mirrors the condition the material unpacker's layer stage
  // uses to build a proxy for that layer, so every object recorded against it in <c>layerMaterialInheritors</c> is
  // guaranteed to resolve to a MATERIAL node in phase 2. RhinoCommon-bound → phase 1 only [ENG-9108].
  private static bool LayerHasRenderMaterial(RhinoLayer layer) =>
    layer.RenderMaterial is not null || layer.RenderMaterialIndex != -1;

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

  // ── Phase 2 (worker thread): pure-Speckle snapshot → BundleBuilder (streams to parquet) ────────────
  [SuppressMessage("Maintainability", "CA1506:Avoid excessive class coupling")]
  private ArtifactBundleBuild WriteBundle(
    CollectedModel model,
    ArtefactSessionLog session,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    ZstdNativeLoader.Ensure(logger); // net48: ensure the parquet Zstd native is loaded (no-op on net8+)
    var bundle = new BundleBuilder(speckleApplication, model.Units);
    try
    {
      WriteInto(bundle, model, session, onOperationProgressed, cancellationToken, out var results);
      session.SetStat("objects", results.Count(r => r.Status == Status.SUCCESS));
      return new ArtifactBundleBuild(bundle, results);
    }
    catch
    {
      bundle.Dispose();
      throw;
    }
  }

  private void WriteInto(
    BundleBuilder bundle,
    CollectedModel model,
    ArtefactSessionLog session,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken,
    out List<SendConversionResult> results
  )
  {
    // Definitions first, so they carry their proper name (a placement only knows the definition id).
    var definitions = new Dictionary<string, BundleDefinition>(StringComparer.Ordinal);
    foreach (var defProxy in model.Definitions)
    {
      string defId = defProxy.applicationId.NotNull();
      definitions[defId] = bundle.GetOrAddDefinition(defId, defProxy.name);
    }
    // Block-definition members render ONLY through their definition (via a placed instance's transform) — never as
    // standalone scene objects [ENG-8782]. They keep their layer (IN_COLLECTION) and properties; their geometry and
    // nested placements are written by the owning definition below (DEFINES / DEFINES_INSTANCE + DEFINES_MEMBER).
    var definitionMemberIds = model.Definitions.SelectMany(d => d.objects).ToHashSet(StringComparer.Ordinal);

    var layers = new Dictionary<int, BundleContainer>();
    var layerByLayerId = new Dictionary<string, BundleContainer>(StringComparer.Ordinal);
    BundleContainer Layer(int layerIndex)
    {
      if (layers.TryGetValue(layerIndex, out var existing))
      {
        return existing;
      }
      CollectedLayer layer = model.Layers[layerIndex];
      BundleContainer? parent =
        layer.ParentIndex is int parentIndex && model.Layers.ContainsKey(parentIndex) ? Layer(parentIndex) : null;
      var container = bundle.GetOrAddContainer(layer.Id, layer.Name, parent, "Layer");
      container.Color = bundle.GetOrAddColor(layer.Argb); // layer colour as a first-class edge [rel 29 NODE_HAS_COLOR]
      layers[layerIndex] = container;
      layerByLayerId[layer.Id] = container;
      return container;
    }

    // object id → its display/solid geometry (for the material + colour passes), placements, and deferred members.
    var geometriesByObjectId = new Dictionary<string, List<BundleGeometry>>(StringComparer.Ordinal);
    var placedObjectIds = new HashSet<string>(StringComparer.Ordinal);
    var memberGeometry = new Dictionary<string, (List<Base> Display, byte[]? Solid)>(StringComparer.Ordinal);
    var memberPlacements = new Dictionary<string, InstanceProxy>(StringComparer.Ordinal);

    // The Collect-phase results are provisional: the write phase can still drop an object's entire geometry
    // (SGEO-unencodable types). Amend those to ERROR so the report card matches the bundle contents [ENG-8826].
    results = model.Results.ToList();
    var resultIndexByAppId = new Dictionary<string, int>(StringComparer.Ordinal);
    for (int i = 0; i < results.Count; i++)
    {
      resultIndexByAppId[results[i].SourceId] = i;
    }

    int count = 0;
    foreach (CollectedObject co in model.Objects)
    {
      cancellationToken.ThrowIfCancellationRequested();
      string id = co.ApplicationId;
      bool isMember = definitionMemberIds.Contains(id);

      var obj = bundle.GetOrAddObject(id);
      obj.SetProperties(
        co.Properties,
        name: co.Name,
        speckleType: co.Converted.speckle_type,
        sourceType: co.SourceType
      );
      obj.Collection = Layer(co.LayerIndex);

      string? dropReason = null;
      if (co.Converted is InstanceProxy instanceProxy)
      {
        // block instance: object → INSTANCE node (transform + definition) via DISPLAY_INSTANCE; a member placement
        // is instead nested under its owning definition (DEFINES_INSTANCE + PLACES), written below.
        if (isMember)
        {
          memberPlacements[id] = instanceProxy;
        }
        else
        {
          var def = definitions.TryGetValue(instanceProxy.definitionId, out var d)
            ? d
            : bundle.GetOrAddDefinition(instanceProxy.definitionId, null);
          obj.Place(def, Flatten(instanceProxy.transform), instanceProxy.units, key: id);
          placedObjectIds.Add(id);
        }
      }
      else
      {
        var (display, solid) = SplitGeometry(co);
        if (isMember)
        {
          memberGeometry[id] = (display, solid);
        }
        else
        {
          dropReason = EmitGeometry(obj, display, solid, geometriesByObjectId);
        }
      }

      if (dropReason is not null && resultIndexByAppId.TryGetValue(id, out int ri))
      {
        results[ri] = new(Status.ERROR, id, co.SourceType, null, new SpeckleException(dropReason));
        session.RecordObject(id, co.SourceType, Status.ERROR, dropReason, 0);
      }
      onOperationProgressed.Report(new("Building", (double)++count / model.Objects.Count));
    }

    // Definition members: geometry rides DEFINES on the member's ordinal (a member's solid + its display meshes share
    // it, so receive can prefer the 3dm per member); nested placements ride DEFINES_INSTANCE + PLACES.
    foreach (var defProxy in model.Definitions)
    {
      var def = definitions[defProxy.applicationId.NotNull()];
      foreach (string memberId in defProxy.objects)
      {
        var member = bundle.GetOrAddObject(memberId);
        if (memberPlacements.TryGetValue(memberId, out var nestedProxy))
        {
          var nested = definitions.TryGetValue(nestedProxy.definitionId, out var n)
            ? n
            : bundle.GetOrAddDefinition(nestedProxy.definitionId, null);
          def.AddMemberPlacement(member, nested, Flatten(nestedProxy.transform), nestedProxy.units);
          placedObjectIds.Add(memberId);
        }
        else if (memberGeometry.TryGetValue(memberId, out var mg))
        {
          int ord = def.NextMemberOrdinal();
          var gs = new List<BundleGeometry>();
          if (mg.Solid is { } solidBytes)
          {
            gs.Add(def.AddMemberRawGeometry(member, solidBytes, RawEncodingFormats.RHINO_3DM, ord));
          }
          gs.AddRange(AddMemberDisplay(def, member, mg.Display, ord, out string? memberSkip));
          geometriesByObjectId[memberId] = gs;
          // Same rule as a standalone object: display fragments present, none encodable, no solid → nothing
          // renderable made the bundle, so the report card must not stand on the Collect-phase SUCCESS.
          if (mg.Display.Count > 0 && gs.Count == 0 && resultIndexByAppId.TryGetValue(memberId, out int mri))
          {
            string reason = memberSkip ?? "no display geometry could be encoded";
            results[mri] = new(Status.ERROR, memberId, results[mri].SourceType, null, new SpeckleException(reason));
            session.RecordObject(memberId, results[mri].SourceType, Status.ERROR, reason, 0);
          }
        }
      }
    }

    EmitAppearance(bundle, model, geometriesByObjectId, placedObjectIds, layerByLayerId);
    EmitGroups(bundle, model.Groups, definitionMemberIds);

    // Named camera viewpoints (collected on the UI thread in phase 1) → envelope.camera_views.parquet.
    foreach (var cameraView in model.CameraViews)
    {
      bundle.CameraView(cameraView);
    }
    // Default scene view = the Rhino layer tree (IN_COLLECTION): BundleBuilder declares it when none is set.
  }

  // Split display meshes from the lossless raw encoding (pure Speckle): Brep/Extrusion/SubD carry BOTH; a plain
  // Mesh/Point/Curve is its own display. A hatch has no IRawEncodedObject, so its Rhino-native 3dm blob (serialized
  // in phase 1) stands in as the raw encoding.
  private static (List<Base> Display, byte[]? Solid) SplitGeometry(CollectedObject co)
  {
    Base rawGeometry = co.Converted;
    List<Base> display;
    RawEncoding? rawEncoding = null;
    if (rawGeometry is IDisplayValue<List<SOG.Mesh>> hasDisplay && rawGeometry is SOG.IRawEncodedObject rawEncoded)
    {
      display = hasDisplay.displayValue.Cast<Base>().ToList();
      rawEncoding = rawEncoded.encodedValue;
    }
    else if (rawGeometry is IDisplayValue<List<SOG.Mesh>> hasDisplayMeshes)
    {
      display = hasDisplayMeshes.displayValue.Cast<Base>().ToList();
    }
    else
    {
      display = [rawGeometry];
    }
    rawEncoding ??= co.RawSolid;
    byte[]? solid =
      rawEncoding is not null && rawEncoding.format == RawEncodingFormats.RHINO_3DM
        ? Convert.FromBase64String(rawEncoding.contents)
        : null;
    return (display, solid);
  }

  // A standalone object: authoritative solid via SOLID (receive-as-solids), display meshes via DISPLAY (viewer).
  // Returns a drop reason when every display fragment was unencodable and no solid landed — nothing renderable
  // made the bundle; an object with no display geometry at all is NOT a drop.
  private string? EmitGeometry(
    BundleObject obj,
    List<Base> display,
    byte[]? solid,
    Dictionary<string, List<BundleGeometry>> geometriesByObjectId
  )
  {
    var gs = new List<BundleGeometry>();
    if (solid is not null)
    {
      gs.Add(obj.AddRawGeometry(solid, RawEncodingFormats.RHINO_3DM, $"{obj.ApplicationId}:solid"));
    }
    string? lastSkip = null;
    int displayCount = 0;
    int ord = 0;
    foreach (Base fragment in display)
    {
      try
      {
        gs.Add(obj.AddGeometry(fragment, fragment.applicationId ?? $"{obj.ApplicationId}:g{ord}"));
        displayCount++;
        ord++;
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        // A display fragment the SGEO encoder doesn't support (text/…) is skipped without failing the whole object —
        // its solid blob + properties still land.
        lastSkip = $"{fragment.speckle_type}: {ex.Message}";
        logger.LogWarning(
          ex,
          "Skipped unsupported display geometry {Type} on {AppId}",
          fragment.speckle_type,
          obj.ApplicationId
        );
      }
    }
    geometriesByObjectId[obj.ApplicationId] = gs;
    return display.Count > 0 && displayCount == 0 && solid is null
      ? lastSkip ?? "no display geometry could be encoded"
      : null;
  }

  private IEnumerable<BundleGeometry> AddMemberDisplay(
    BundleDefinition def,
    BundleObject member,
    List<Base> display,
    int ord,
    out string? lastSkip
  )
  {
    lastSkip = null;
    var encodable = new List<Base>(display.Count);
    foreach (Base fragment in display)
    {
      // Probe encodability up front so one unsupported fragment doesn't abort the member's whole geometry list.
      if (SgeoEncoder.TryGetPrimitiveType(fragment, out _))
      {
        encodable.Add(fragment);
      }
      else
      {
        lastSkip = $"{fragment.speckle_type}: not SGEO-encodable";
        logger.LogWarning(
          "Skipped unsupported display geometry {Type} on member {AppId}",
          fragment.speckle_type,
          member.ApplicationId
        );
      }
    }
    return def.AddMember(member, encodable, ord);
  }

  // Render materials → the geometry plane (HAS_MATERIAL) on each object's display/solid geometry; a material painted
  // directly on a block placement rides the object plane (OBJECT_HAS_MATERIAL, rel 26); a layer-sourced material is
  // both the authored layer assignment (NODE_HAS_MATERIAL, rel 28) and, since a layer owns no geometry, a HAS_MATERIAL
  // on every object inheriting it (MaterialFromLayer) [ENG-9108]. Colours: same three planes, minus the layer one
  // (layer colours already ride the layer containers).
  private static void EmitAppearance(
    BundleBuilder bundle,
    CollectedModel model,
    IReadOnlyDictionary<string, List<BundleGeometry>> geometriesByObjectId,
    HashSet<string> placedObjectIds,
    IReadOnlyDictionary<string, BundleContainer> layerByLayerId
  )
  {
    var inheritorsByLayerId = new Dictionary<string, List<string>>(StringComparer.Ordinal);
    foreach (var kv in model.LayerMaterialInheritors)
    {
      if (!inheritorsByLayerId.TryGetValue(kv.Value, out var inheritors))
      {
        inheritorsByLayerId[kv.Value] = inheritors = new List<string>();
      }
      inheritors.Add(kv.Key);
    }

    foreach (var materialProxy in model.Materials)
    {
      var value = materialProxy.value;
      var material = bundle.GetOrAddMaterial(
        materialProxy.applicationId.NotNull(),
        value.name,
        value.diffuse,
        value.opacity,
        value.metalness,
        value.roughness,
        value.emissive,
        value["ior"] as double? // dynamic prop set by RhinoMaterialUnpacker (PBR IndexOfRefraction) [ENG-8791]
      );
      foreach (var objectId in materialProxy.objects)
      {
        if (geometriesByObjectId.TryGetValue(objectId, out var gs))
        {
          foreach (var g in gs)
          {
            g.Material = material;
          }
        }
        else if (placedObjectIds.Contains(objectId))
        {
          bundle.GetOrAddObject(objectId).Material = material;
        }
        else if (layerByLayerId.TryGetValue(objectId, out var layer))
        {
          layer.Material = material;
          if (inheritorsByLayerId.TryGetValue(objectId, out var inheritors))
          {
            foreach (var inheritorId in inheritors)
            {
              if (geometriesByObjectId.TryGetValue(inheritorId, out var inheritorGs))
              {
                foreach (var g in inheritorGs)
                {
                  g.Material = material;
                }
              }
            }
          }
        }
      }
    }

    foreach (var colorProxy in model.Colors)
    {
      var color = bundle.GetOrAddColor(colorProxy.value);
      foreach (var objectId in colorProxy.objects)
      {
        if (geometriesByObjectId.TryGetValue(objectId, out var gs))
        {
          foreach (var g in gs)
          {
            g.Color = color;
          }
        }
        else if (placedObjectIds.Contains(objectId))
        {
          // OVERRIDE semantics: the placement's colour beats the definition geometry's own [ENG-8825].
          bundle.GetOrAddObject(objectId).Color = color;
        }
      }
    }
  }

  // Authored scene groups → CONTAINER("Group") nodes + IN_GROUP membership: a SEPARATE axis from IN_COLLECTION (an
  // object keeps its layer AND its groups; memberships overlap). Definition members get no scene edges, so a group
  // emptied by that is skipped entirely.
  private static void EmitGroups(
    BundleBuilder bundle,
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
      var container = bundle.GetOrAddContainer(group.Id, group.Name, null, "Group");
      int ord = 0;
      foreach (string memberId in memberIds)
      {
        bundle.GetOrAddObject(memberId).AddToGroup(container, ord++);
      }
    }
  }

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
    // object id → the id of the layer whose render material it inherits (MaterialFromLayer only) [ENG-9108]
    IReadOnlyDictionary<string, string> LayerMaterialInheritors,
    IReadOnlyList<SendConversionResult> Results
  );
}
