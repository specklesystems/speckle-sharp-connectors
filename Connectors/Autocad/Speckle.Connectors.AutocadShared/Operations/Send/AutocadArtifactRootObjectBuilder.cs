using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Autodesk.AutoCAD.DatabaseServices;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Autocad.HostApp;
using Speckle.Connectors.Autocad.HostApp.Extensions;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Diagnostics;
using Speckle.Connectors.Common.Operations;
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
using Speckle.Sdk.Pipelines;
using Speckle.Sdk.Pipelines.Progress;
using Speckle.Sdk.Pipelines.Send.Artifacts;
#if NETFRAMEWORK
using System.IO; // net8+ provides this via ImplicitUsings; net48 needs it explicitly.
#endif

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
/// without subclassing. Deferred this pass: Civil3D property-set definitions.</para>
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
      collected = await threadContext.RunOnMainAsync(() =>
        Task.FromResult(CollectOnMain(objects, session, onOperationProgressed, cancellationToken))
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

    // Materials/colors are keyed by name and list OBJECT ids (resolved to geometry K(s) in phase 2). Only explicit
    // object-level sources are emitted (the unpacker skips ByLayer) — layer colours ride the layer COLLECTION nodes'
    // argb instead (mirrors Rhino), so ByLayer objects inherit the restored layer colour on receive with no edge.
    var materials = materialUnpacker.UnpackMaterials(atomicObjects, new List<LayerTableRecord>());
    var colors = colorUnpacker.UnpackColors(atomicObjects, new List<LayerTableRecord>());
    var layerArgbByName = CollectLayerColors(atomicObjects);

    // Authored scene groups → plain snapshot records (Base-free — no GroupProxy). Membership lists OBJECT ids.
    var groups = CollectGroups(atomicObjects);

    return new CollectedModel(
      units,
      collectedObjects,
      materials,
      colors,
      layerArgbByName,
      instanceDefinitionProxies,
      groups,
      results
    );
  }

  // AutoCAD groups → plain snapshot records. Membership rides persistent reactors on each entity (a Group is a
  // reactor on its members); AutoCAD groups don't nest, so each membership is one flat IN_GROUP edge.
  // AutoCAD-API-bound (transaction) → phase 1 only.
  private List<CollectedGroup> CollectGroups(List<AutocadRootObject> atomicObjects)
  {
    var groups = new Dictionary<string, CollectedGroup>(StringComparer.Ordinal);
    using var transaction = converterSettings.Current.Document.Database.TransactionManager.StartTransaction();
    foreach (var (dbObject, applicationId) in atomicObjects)
    {
      try
      {
        foreach (ObjectId reactorId in dbObject.GetPersistentReactorIds())
        {
          if (transaction.GetObject(reactorId, OpenMode.ForRead) is not Group group)
          {
            continue;
          }
          string groupAppId = group.GetSpeckleApplicationId();
          if (!groups.TryGetValue(groupAppId, out CollectedGroup? collected))
          {
            collected = new CollectedGroup(groupAppId, group.Name, new List<string>());
            groups[groupAppId] = collected;
          }
          collected.MemberIds.Add(applicationId);
        }
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        logger.LogWarning(ex, "Failed to unpack groups for {AppId}", applicationId);
      }
    }
    return groups.Values.ToList();
  }

  // Captures the colour of every layer used by the selection (AutoCAD API is document-bound → phase 1 only), so
  // phase 2 can stamp each layer COLLECTION node's argb — the layer half of colour inheritance.
  private Dictionary<string, int> CollectLayerColors(List<AutocadRootObject> atomicObjects)
  {
    var result = new Dictionary<string, int>(StringComparer.Ordinal);
    try
    {
      var db = converterSettings.Current.Document.Database;
      using var tr = db.TransactionManager.StartTransaction();
      var layerTable = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
      foreach (var (entity, _) in atomicObjects)
      {
        string layerName = entity.Layer;
        if (result.ContainsKey(layerName) || !layerTable.Has(layerName))
        {
          continue;
        }
        var record = (LayerTableRecord)tr.GetObject(layerTable[layerName], OpenMode.ForRead);
        result[layerName] = record.Color.ColorValue.ToArgb();
      }
      tr.Commit();
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      logger.LogError(ex, "Failed to collect layer colours for the artefact bundle");
    }
    return result;
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
      var instanceProps =
        instanceProxy["properties"] as Dictionary<string, object?> ?? new Dictionary<string, object?>();
      return new CollectedObject(
        applicationId,
        sourceType,
        entity.Layer,
        instanceProps,
        instanceProxy,
        entity.Color.IsByLayer
      );
    }

    Base converted = converter.Convert(entity);
    // AutoCAD wraps entities in AutocadObject, Civil3D in Civil3dObject — both are DataObjects carrying the
    // converted properties + display meshes.
    var properties = converted is DataObject dataObject ? dataObject.properties : new Dictionary<string, object?>();
    return new CollectedObject(applicationId, sourceType, entity.Layer, properties, converted, entity.Color.IsByLayer);
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

    var layerCollectionKByName = new Dictionary<string, int>(StringComparer.Ordinal);
    var geometryKsByObjectId = new Dictionary<string, List<int>>(StringComparer.Ordinal);
    var instanceKByObjectId = new Dictionary<string, int>(StringComparer.Ordinal);

    // Block-definition members are interned as atomic objects too, but they render ONLY through their definition
    // (via a placed instance's transform). They get NO standalone top-level render edges — suppressed in EmitObject.
    var definitionMemberIds = model.Definitions.SelectMany(d => d.objects).ToHashSet(StringComparer.Ordinal);

    // The Collect-phase results are provisional: the write phase can still drop an object's entire geometry
    // (SGEO-unencodable types). Amend those to ERROR so the report card matches the bundle contents [ENG-8826].
    var results = model.Results.ToList();
    var resultIndexByAppId = new Dictionary<string, int>(StringComparer.Ordinal);
    for (int i = 0; i < results.Count; i++)
    {
      resultIndexByAppId[results[i].SourceId] = i;
    }

    int count = 0;
    foreach (CollectedObject co in model.Objects)
    {
      cancellationToken.ThrowIfCancellationRequested();
      int collK = GetOrAddLayerCollection(pipeline, co.LayerName, model.LayerArgbByName, layerCollectionKByName);
      // A ByLayer DEFINITION MEMBER sits outside the layer-container inheritance (it rides DEFINES, no
      // IN_COLLECTION), so a consumer would fall back to the placing instance's colour — wrong in AutoCAD
      // semantics, where ByLayer keeps the member's own layer colour. Resolve it at send time [ENG-8825].
      int? memberLayerArgb =
        co.ColorIsByLayer && model.LayerArgbByName.TryGetValue(co.LayerName, out int layerArgb) ? layerArgb : null;
      string? dropReason = EmitObject(
        pipeline,
        co,
        collK,
        model.Units,
        geometryKsByObjectId,
        instanceKByObjectId,
        definitionMemberIds,
        memberLayerArgb
      );
      if (dropReason is not null && resultIndexByAppId.TryGetValue(co.ApplicationId, out int ri))
      {
        results[ri] = new(Status.ERROR, co.ApplicationId, co.SourceType, null, new SpeckleException(dropReason));
        session.RecordObject(co.ApplicationId, co.SourceType, Status.ERROR, dropReason, 0);
      }
      onOperationProgressed.Report(new("Building", (double)++count / model.Objects.Count));
    }

    EmitValueNodes(pipeline, model, geometryKsByObjectId, instanceKByObjectId);
    EmitGroups(pipeline, model.Groups, definitionMemberIds);
    EmitCivilNetworkTopology(pipeline, model.Objects);
    EmitAdditionalNodes(pipeline);

    // Default scene view: the (flat) AutoCAD layer namespace via IN_COLLECTION.
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
    session.SetStat("definitions", model.Definitions.Count);
    session.SetStat("materials", model.Materials.Count);
    session.SetStat("layers", layerCollectionKByName.Count);
    session.SetStat("groups", model.Groups.Count);

    logger.LogInformation("Built artefact bundle: {fileCount} files, {objectCount} objects", bundle.Count, objectCount);
    return new BundleResult(bundle, rootId, objectCount, results);
  }

  // Emits one object: eav labels + IN_COLLECTION, then a block placement (DISPLAY_INSTANCE → INSTANCE node) or
  // geometry — the lossless SAT SOLID blob (if present) plus the DISPLAY meshes. Pure Speckle (no AutoCAD API).
  // Returns null on success, or a drop reason when the object had display geometry but NONE of it could be
  // encoded (and no solid landed) — the caller downgrades the object's Collect-phase SUCCESS [ENG-8826].
  private string? EmitObject(
    ObjectsArtifactPipeline pipeline,
    CollectedObject co,
    int collK,
    string units,
    Dictionary<string, List<int>> geometryKsByObjectId,
    Dictionary<string, int> instanceKByObjectId,
    HashSet<string> definitionMemberIds,
    int? memberLayerArgb
  )
  {
    // A block-definition member renders ONLY through its definition (via a placed instance's transform), so it gets
    // NO standalone top-level render edge (DISPLAY / DISPLAY_INSTANCE / SOLID) and NO scene-tree membership
    // (IN_COLLECTION). Its geometry/instance K is still registered below so DEFINES / DEFINES_INSTANCE resolve.
    bool isDefinitionMember = definitionMemberIds.Contains(co.ApplicationId);

    int objK = pipeline.InternObject(co.ApplicationId);
    if (!isDefinitionMember)
    {
      pipeline.InCollection(objK, collK, 0);
    }
    pipeline.AddProperties(
      co.ApplicationId,
      co.Properties,
      RootScalars(co.Converted.speckle_type, co.SourceType, units, co.SourceType)
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
      return null;
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

    // Authoritative solid: the raw ACIS-SAT blob, kept verbatim for receive-as-solids. A standalone object links it
    // via the SOLID rel (tracked in hasSolid so a mesh-less solid isn't reported as a drop [ENG-8826]); a definition
    // member instead lets it ride DEFINES (added to gKs below) so the block reconstructs the native Solid3d, not
    // just its display mesh [ENG-8855] — but a member still gets NO standalone SOLID edge (it renders only through
    // a placed instance's transform). Mirrors the Rhino builder.
    bool hasSolid = false;
    int? memberSolidK = null;
    if (rawEncoding is not null && rawEncoding.format == RawEncodingFormats.ACAD_SAT)
    {
      byte[] solidBytes = Convert.FromBase64String(rawEncoding.contents);
      int solidK = pipeline.AddRawGeometry($"{co.ApplicationId}:solid", solidBytes, RawEncodingFormats.ACAD_SAT);
      if (isDefinitionMember)
      {
        memberSolidK = solidK;
      }
      else
      {
        pipeline.Solid(objK, solidK, 0);
        hasSolid = true;
      }
    }

    // Renderable display meshes (and self-display primitives the SGEO encoder supports: points/curves). A bad
    // fragment is isolated (poison-element rule: it must never abort the send) — but its reason is kept so a
    // fully-dropped object can be reported instead of silently claiming SUCCESS.
    var gKs = new List<int>();
    string? lastSkip = null;
    if (memberSolidK is int msk)
    {
      gKs.Add(msk); // member's solid rides DEFINES alongside its display meshes; receive prefers the SAT per member
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
        lastSkip = $"{fragment.speckle_type}: {ex.Message}";
        logger.LogWarning(
          ex,
          "Skipped unsupported display geometry {Type} on {AppId}",
          fragment.speckle_type,
          co.ApplicationId
        );
      }
    }

    geometryKsByObjectId[co.ApplicationId] = gKs;

    EmitMemberLayerColor(pipeline, isDefinitionMember, memberLayerArgb, gKs);

    // Every display fragment was dropped and no solid landed → the bundle carries nothing renderable for this
    // object; report it instead of standing on the Collect-phase SUCCESS. An object that never had display
    // geometry (e.g. a Civil3D parent whose content rides its children) is NOT a drop.
    string? dropReason =
      displayGeometry.Count > 0 && gKs.Count == 0 && !hasSolid
        ? lastSkip ?? "no display geometry could be encoded"
        : null;

    // Civil3D sub-object tree (Civil3dObject.elements): corridor → baseline → region → applied assembly →
    // subassembly; alignment → profiles; site → parcels/feature-lines. The converter builds this graph but the
    // display-only path drops it. Intern each child, emit its geometry, and link it to its parent with a SUBELEMENT
    // edge (guarded against re-emitting geometry for a child that's also a top-level object).
    if (co.Converted is Civil3dObject civilParent)
    {
      int childOrd = 0;
      foreach (Base child in civilParent.elements)
      {
        EmitCivilChild(pipeline, child, collK, units, objK, childOrd++, geometryKsByObjectId);
      }
      if (childOrd > 0)
      {
        return null; // the parent's content rides its emitted children — not a drop even if its own display failed
      }
    }

    return dropReason;
  }

  // ByLayer member: pin its resolved layer colour onto its geometry so it doesn't inherit the instance's
  // colour override — the fallback stays reserved for ByBlock members [ENG-8825].
  private static void EmitMemberLayerColor(
    ObjectsArtifactPipeline pipeline,
    bool isDefinitionMember,
    int? memberLayerArgb,
    List<int> gKs
  )
  {
    if (!isDefinitionMember || memberLayerArgb is not int mla || gKs.Count == 0)
    {
      return;
    }
    int layerColorK = pipeline.AddColor(mla);
    foreach (var gK in gKs)
    {
      pipeline.HasColor(gK, layerColorK);
    }
  }

  // One Civil3D child in the .elements tree. Emits a SUBELEMENT edge always; interns + emits the child's own
  // geometry/properties only once (a child may also be a top-level object — geometryKsByObjectId is the guard);
  // recurses into its own .elements.
  private void EmitCivilChild(
    ObjectsArtifactPipeline pipeline,
    Base child,
    int collK,
    string units,
    int parentObjK,
    int subOrd,
    Dictionary<string, List<int>> geometryKsByObjectId
  )
  {
    string childAppId = child.applicationId ?? Guid.NewGuid().ToString();
    int childK = pipeline.InternObject(childAppId);
    pipeline.Subelement(parentObjK, childK, subOrd);

    if (!geometryKsByObjectId.ContainsKey(childAppId))
    {
      pipeline.InCollection(childK, collK, 0);
      var props = child is DataObject d ? d.properties : new Dictionary<string, object?>();
      string childName = child is DataObject dn ? dn.name : child.speckle_type;
      string childType = child is Civil3dObject ct ? ct.type : child.speckle_type;
      pipeline.AddProperties(childAppId, props, RootScalars(child.speckle_type, childName, units, childType));

      var display = child is Civil3dObject cc ? new List<Base>(cc.displayValue) : new List<Base> { child };
      if (child is Civil3dObject cb && cb.baseCurves is { Count: > 0 } baseCurves)
      {
        display.AddRange(baseCurves.Cast<Base>());
      }
      var gKs = new List<int>();
      int ord = 0;
      foreach (Base fragment in display)
      {
        try
        {
          int gK = pipeline.AddGeometry(fragment.applicationId ?? $"{childAppId}:g{ord}", fragment);
          pipeline.Display(childK, gK, ord++);
          gKs.Add(gK);
        }
        catch (Exception ex) when (!ex.IsFatal())
        {
          logger.LogWarning(
            ex,
            "Skipped unsupported Civil3D child geometry {Type} on {AppId}",
            fragment.speckle_type,
            childAppId
          );
        }
      }
      geometryKsByObjectId[childAppId] = gKs;
    }

    if (child is Civil3dObject civilChild)
    {
      int grandOrd = 0;
      foreach (Base grandChild in civilChild.elements)
      {
        EmitCivilChild(pipeline, grandChild, collK, units, childK, grandOrd++, geometryKsByObjectId);
      }
    }
  }

  // Civil3D pipe-network topology, grounded in the resolved "Assignments" property app-ids (ClassPropertiesExtractor):
  //   IN_SYSTEM    part (pipe/structure) → its pipe network (a CONTAINER, subtype "Network").
  //   CONNECTS_TO  pipe → its start/end structure (guarded to sent objects; a structure not in the send set is skipped).
  // Non-Civil3D objects carry no "Assignments" dict and are skipped. InternObject is idempotent, so re-resolving an
  // object's K here is a lookup, not a new object.
  private static void EmitCivilNetworkTopology(ObjectsArtifactPipeline pipeline, IReadOnlyList<CollectedObject> objects)
  {
    var sent = objects.Select(o => o.ApplicationId).ToHashSet(StringComparer.Ordinal);
    var networkKById = new Dictionary<string, int>(StringComparer.Ordinal);
    foreach (var co in objects)
    {
      if (!co.Properties.TryGetValue("Assignments", out var a) || a is not IDictionary<string, object?> assign)
      {
        continue;
      }
      int objK = pipeline.InternObject(co.ApplicationId);

      if (assign.TryGetValue("networkId", out var nid) && nid is string networkId && networkId.Length > 0)
      {
        if (!networkKById.TryGetValue(networkId, out int netK))
        {
          netK = pipeline.AddContainer(
            networkId,
            assign.TryGetValue("networkName", out var nn) ? nn as string : null,
            null,
            "Network"
          );
          networkKById[networkId] = netK;
        }
        pipeline.InSystem(objK, netK, 0);
      }

      foreach (var key in s_structureKeys)
      {
        if (
          assign.TryGetValue(key, out var sid)
          && sid is string structId
          && structId.Length > 0
          && sent.Contains(structId)
        )
        {
          pipeline.ConnectsTo(objK, pipeline.InternObject(structId));
        }
      }
    }
  }

  private static readonly string[] s_structureKeys = { "startStructureId", "endStructureId" };

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
      int memberOrd = 0;
      foreach (var memberId in defProxy.objects)
      {
        if (instanceKByObjectId.TryGetValue(memberId, out var instK))
        {
          pipeline.DefinesInstance(defK, instK, memberOrd);
        }
        else if (geometryKsByObjectId.TryGetValue(memberId, out var memberGKs))
        {
          // All geometry of one member shares its member ordinal, so receive can group the member's authoritative SAT
          // solid with its display mesh(es) and prefer the solid (see AutocadHostObjectArtefactBuilder.BuildDefinitions).
          foreach (var gK in memberGKs)
          {
            pipeline.Defines(defK, gK, memberOrd);
          }
        }
        memberOrd++;
      }
    }

    // 2) render materials → HAS_MATERIAL (geometry → material node). Material proxies list OBJECT ids.
    foreach (var materialProxy in model.Materials)
    {
      var value = materialProxy.value;
      int matK = pipeline.AddMaterial(
        materialProxy.applicationId.NotNull(),
        value.name,
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

    // 3) object colors → HAS_COLOR (geometry → color node). Color proxies list OBJECT ids. A block INSTANCE has
    // no geometry of its own (it enters instanceKByObjectId, not geometryKsByObjectId), so its colour edge is
    // emitted OBJECT-sourced instead — spec HAS_COLOR src is geometry|object, and the viewer looks up both
    // namespaces — so per-placement colour overrides survive [ENG-8825].
    foreach (var colorProxy in model.Colors)
    {
      // A "block"-sourced proxy is INHERITANCE, not an explicit colour: a ByBlock member must take its placing
      // instance's colour — exactly the object-sourced edge below — and the unpacker itself notes ByBlock's
      // ColorValue is garbage (near-black/white). Emitting it as an explicit edge pinned members to white.
      if (colorProxy["source"] is "block")
      {
        continue;
      }
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
          // srcIsObject: the object and geometry K-spaces overlap numerically, so the edge carries a namespace
          // tag (ord=1) — without it receive can't tell this from a geometry-sourced colour [ENG-8822].
          pipeline.HasColor(pipeline.InternObject(objectId), colorK, srcIsObject: true);
        }
      }
    }
  }

  protected virtual void EmitAdditionalNodes(ObjectsArtifactPipeline pipeline) { }

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

  // Resolves (and interns once) the flat COLLECTION node for a layer name (with the layer's colour as its argb).
  // AutoCAD has no nested layers.
  private static int GetOrAddLayerCollection(
    ObjectsArtifactPipeline pipeline,
    string layerName,
    IReadOnlyDictionary<string, int> layerArgbByName,
    Dictionary<string, int> cache
  )
  {
    if (cache.TryGetValue(layerName, out var existing))
    {
      return existing;
    }
    int? argb = layerArgbByName.TryGetValue(layerName, out int a) ? a : null;
    int collK = pipeline.AddCollection(layerName, layerName, null, "Layer", argb);
    cache[layerName] = collK;
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
  private sealed record CollectedObject(
    string ApplicationId,
    string SourceType,
    string LayerName,
    Dictionary<string, object?> Properties,
    Base Converted, // InstanceProxy for block instances, otherwise the AutocadObject carrier (display meshes + SAT)
    bool ColorIsByLayer = false // captured on the UI thread; drives member layer-colour resolution [ENG-8825]
  );

  private sealed record CollectedGroup(string Id, string? Name, List<string> MemberIds);

  private sealed record CollectedModel(
    string Units,
    IReadOnlyList<CollectedObject> Objects,
    IReadOnlyList<RenderMaterialProxy> Materials,
    IReadOnlyList<ColorProxy> Colors,
    IReadOnlyDictionary<string, int> LayerArgbByName,
    IReadOnlyList<InstanceDefinitionProxy> Definitions,
    IReadOnlyList<CollectedGroup> Groups,
    IReadOnlyList<SendConversionResult> Results
  );

  private sealed record BundleResult(
    IReadOnlyDictionary<string, string> Bundle,
    string RootId,
    int ObjectCount,
    IReadOnlyList<SendConversionResult> Results
  );
}
