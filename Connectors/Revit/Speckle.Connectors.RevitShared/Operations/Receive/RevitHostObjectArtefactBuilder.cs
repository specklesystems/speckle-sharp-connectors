using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Autodesk.Revit.DB;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Diagnostics;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.Revit.HostApp;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Services;
using Speckle.Converters.RevitShared.Settings;
using Speckle.DoubleNumerics;
using Speckle.Objects.Utils;
using Speckle.Sdk;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models;
using Speckle.Sdk.Pipelines;
using Speckle.Sdk.Pipelines.Progress;
using Speckle.Sdk.Pipelines.Receive.Artifacts;
using RenderMaterial = Speckle.Objects.Other.RenderMaterial;
using SMesh = Speckle.Objects.Geometry.Mesh;

namespace Speckle.Connectors.Revit.Operations.Receive;

/// <summary>
/// Bakes a Speckle 4.0 artefact <see cref="ArtefactBundle"/> <b>directly</b> into the Revit document — talking only
/// to the neutral dense-int graph + raw Revit API, skipping the v1 <c>RootObjectUnpacker</c> / traversal / per-type
/// converter dispatch. SGEO meshes are tessellated straight into geometry (mirroring <c>MeshConverterToHost</c>);
/// non-mesh SGEO primitives (points, curves, regions, …) decode to a Speckle geometry object and convert via the
/// shared Revit ToHost geometry converter. Materials are created directly from MATERIAL nodes. The receive-side
/// twin of the send-side <c>RevitArtifactRootObjectBuilder</c>.
/// </summary>
/// <remarks>
/// <para><b>Grouping.</b> Revit has no layers; each baked element is stamped with its model marker in the Comments
/// parameter so a re-receive can collect-and-delete the prior bake cheaply (a deliberate choice to avoid the slow v1
/// Revit-Group baking). Element category comes from the object's <c>category</c>/<c>builtInCategory</c> property.</para>
/// <para><b>ReceiveInstancesAsFamilies (ENG-9101).</b> Atomic objects always bake as <see cref="DirectShape"/>s,
/// regardless of the setting. Only instance/definition handling branches: off bakes <see cref="DirectShape"/>
/// instances via the <see cref="DirectShapeLibrary"/> (<see cref="BakeInstances"/>); on bakes real Revit families
/// via <see cref="RevitFamilyBaker"/> (<see cref="BakeInstancesAsFamilies"/>), reading the same
/// DEFINES/DEFINES_INSTANCE/DISPLAY_INSTANCE edges and the same <see cref="DecodeGeometry"/>-derived geometry
/// selection. Neither branch reconstructs a <c>Base</c> graph or delegates to the v1
/// <see cref="RevitHostObjectBuilder"/>.</para>
/// </remarks>
public sealed class RevitHostObjectArtefactBuilder : IArtifactHostObjectBuilder
{
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private readonly IThreadContext _threadContext;
  private readonly ITransactionManager _transactionManager;
  private readonly ScalingServiceToHost _scalingService;
  private readonly IReferencePointConverter _referencePointConverter;
  private readonly RevitUtils _revitUtils;
  private readonly ISdkActivityFactory _activityFactory;
  private readonly ILogger<RevitHostObjectArtefactBuilder> _logger;
  private readonly RevitGroupBaker _groupBaker;
  private readonly RevitViewBaker _viewBaker;
  private readonly ITypedConverter<Base, List<GeometryObject>> _geometryConverter;
  private readonly RevitFamilyBaker _familyBaker;
  private readonly ITypedConverter<SMesh, GeometryObject> _freeformMeshConverter;

  // Set by DecodeGeometry when the non-mesh SGEO fallback fails; surfaced by callers as the ERROR reason for an
  // empty geometry result instead of the generic "did not convert to any native geometry" message.
  private string? _lastDecodeFailure;

  public RevitHostObjectArtefactBuilder(
    IConverterSettingsStore<RevitConversionSettings> converterSettings,
    IThreadContext threadContext,
    ITransactionManager transactionManager,
    ScalingServiceToHost scalingService,
    IReferencePointConverter referencePointConverter,
    RevitUtils revitUtils,
    ISdkActivityFactory activityFactory,
    ILogger<RevitHostObjectArtefactBuilder> logger,
    RevitGroupBaker groupBaker,
    RevitViewBaker viewBaker,
    ITypedConverter<Base, List<GeometryObject>> geometryConverter,
    RevitFamilyBaker familyBaker,
    ITypedConverter<SMesh, GeometryObject> freeformMeshConverter
  )
  {
    _converterSettings = converterSettings;
    _threadContext = threadContext;
    _transactionManager = transactionManager;
    _scalingService = scalingService;
    _referencePointConverter = referencePointConverter;
    _revitUtils = revitUtils;
    _activityFactory = activityFactory;
    _logger = logger;
    _groupBaker = groupBaker;
    _viewBaker = viewBaker;
    _geometryConverter = geometryConverter;
    _familyBaker = familyBaker;
    _freeformMeshConverter = freeformMeshConverter;
  }

  public Task<HostObjectBuilderResult> Build(
    ArtefactBundle bundle,
    string projectName,
    string modelName,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  ) =>
    // Revit API is main-thread-affine and mutations require a transaction → everything runs on the main thread.
    _threadContext.RunOnMain(() => BakeAll(bundle, projectName, modelName, onOperationProgressed, cancellationToken));

  [SuppressMessage("Maintainability", "CA1506:Avoid excessive class coupling")]
  private HostObjectBuilderResult BakeAll(
    ArtefactBundle bundle,
    string projectName,
    string modelName,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    var marker = $"Project {projectName}: Model {modelName}";
    using var activity = _activityFactory.Start("Build (artefact)");
    using var session = ArtefactSessionLog.Start(
      "Revit",
      ArtefactDirection.Receive,
      projectName,
      modelName,
      null,
      _logger
    );
    session.SetStat("objects", bundle.ObjectAppIds.Count);
    session.SetStat("geometryBlobs", bundle.Geometries.Count);
    session.SetStat("definitions", bundle.Nodes.Values.Count(n => n.Kind == NodeKind.Definition));
    session.SetStat("instanceNodes", bundle.Nodes.Values.Count(n => n.Kind == NodeKind.Instance));
    session.SetStat("cameraViews", bundle.CameraViews.Count);

    var doc = _converterSettings.Current.Document;
    var rels = bundle.Relations;

    // ENG-8808 / ENG-9099: undo/redo the sender's reference-point re-basing (translation + rotation, e.g. Shared
    // Coordinates' true-north angle). The send pipeline baked its main-model reference-point transform into
    // geometry and recorded it in the bundle; compose it with the receiver's own reference-point setting
    // (Source = no local transform → apply the sender's as-is, restoring the source model's internal coordinates)
    // and push it so ToInternalPoints/ConvertToInternalCoordinates re-bases every atomic vertex, and
    // BuildInstanceTransform re-bases every instance placement. Mirrors v1 RevitHostObjectBuilder's composition.
    // No recorded transform + no local setting = no-op.
    var sourceReferencePoint = ReadSourceReferencePointTransform(bundle);
    using var referencePointScope = _converterSettings.Push(s =>
      s with
      {
        ReferencePointTransform = ReferencePointHelper.CalculateNewTransform(
          s.ReferencePointTransform,
          sourceReferencePoint
        ),
      }
    );

    var bakedObjectIds = new List<string>();
    var conversionResults = new HashSet<ReceiveConversionResult>();

    // 0 — clean a previous receive of this model (delete marked DirectShapes; reset the geometry-instance library).
    _transactionManager.StartTransaction(true, "Pre receive clean");
    try
    {
      PreClean(doc, marker);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, "Artefact receive pre-clean failed for '{Marker}'", marker);
    }
    _transactionManager.CommitTransaction();

    var validCategories = BuildValidDirectShapeCategories(doc);
    var categoryCache = new Dictionary<string, ElementId>(StringComparer.Ordinal);

    _transactionManager.StartTransaction(true, "Speckle receive (artefact)");
    try
    {
      // 1 — materials (MATERIAL nodes → Revit Material elements), then geometry → material id resolution.
      onOperationProgressed.Report(new("Converting materials", null));
      Dictionary<int, ElementId> materialIdByNode;
      using (session.Phase("Materials"))
      {
        materialIdByNode = CreateMaterials(doc, bundle);
      }
      var materialIdByGeometry = new Dictionary<int, ElementId>();
      foreach (var kv in rels.MaterialByGeometry)
      {
        if (materialIdByNode.TryGetValue(kv.Value, out var mid))
        {
          materialIdByGeometry[kv.Key] = mid;
        }
      }

      // 2 — atomic geometry (objects with a direct DISPLAY). Instances handled in step 3.
      using (session.Phase("Atomic"))
      {
        BakeAtomic(
          doc,
          bundle,
          rels,
          materialIdByGeometry,
          validCategories,
          categoryCache,
          marker,
          bakedObjectIds,
          conversionResults,
          session,
          onOperationProgressed,
          cancellationToken
        );
      }

      // 3 — instances: build definitions from DEFINES → geometry, place one per DISPLAY_INSTANCE edge. Only this
      // step branches on ReceiveInstancesAsFamilies — atomic objects above are unaffected by the setting.
      if (rels.DisplayInstanceEdges.Count > 0)
      {
        onOperationProgressed.Report(new("Converting instances", null));
        using (session.Phase("Instances"))
        {
          if (_converterSettings.Current.ReceiveInstancesAsFamilies)
          {
            BakeInstancesAsFamilies(
              doc,
              bundle,
              rels,
              materialIdByNode,
              marker,
              bakedObjectIds,
              conversionResults,
              session,
              cancellationToken
            );
          }
          else
          {
            BakeInstances(
              doc,
              bundle,
              rels,
              materialIdByGeometry,
              validCategories,
              categoryCache,
              marker,
              bakedObjectIds,
              conversionResults,
              session,
              cancellationToken
            );
          }
        }
      }

      // 4 — named camera viewpoints (envelope.camera_views.parquet) → View3D per row.
      if (bundle.CameraViews.Count > 0)
      {
        onOperationProgressed.Report(new("Converting camera views", null));
        using (session.Phase("Views"))
        {
          BakeCameraViews(bundle, marker, bakedObjectIds, conversionResults, session);
        }
      }

      _transactionManager.CommitTransaction();
    }
    catch
    {
      _transactionManager.RollbackTransaction();
      throw;
    }

    return new HostObjectBuilderResult(bakedObjectIds, conversionResults);
  }

  // ── atomic objects ────────────────────────────────────────────────────────────────────────────────────
  [SuppressMessage("Maintainability", "CA1506:Avoid excessive class coupling")]
  private void BakeAtomic(
    Document doc,
    ArtefactBundle bundle,
    ArtefactRelations rels,
    Dictionary<int, ElementId> materialIdByGeometry,
    Dictionary<string, ElementId> validCategories,
    Dictionary<string, ElementId> categoryCache,
    string marker,
    List<string> bakedObjectIds,
    HashSet<ReceiveConversionResult> conversionResults,
    ArtefactSessionLog session,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    int count = 0;
    int total = bundle.ObjectAppIds.Count;
    foreach (var kv in bundle.ObjectAppIds)
    {
      cancellationToken.ThrowIfCancellationRequested();
      onOperationProgressed.Report(new("Converting objects", (double)++count / total));
      int objK = kv.Key;
      string appId = kv.Value;

      if (rels.DisplayByObject(objK) is not { Count: > 0 } displayEdges)
      {
        // an instance placement (step 3) or a non-geometric element (room/level/area) → skip, don't error.
        if (!rels.DisplayInstanceByObject.ContainsKey(objK))
        {
          session.Increment("nonGeometricSkipped");
        }
        continue;
      }

      bundle.Properties.TryGetValue(objK, out var props);
      var source = Source(appId);
      var srcType = SrcType(props);
      var sw = Stopwatch.StartNew();
      try
      {
        _lastDecodeFailure = null;
        var geometry = new List<GeometryObject>();
        foreach (var edge in displayEdges.OrderBy(e => e.Ord))
        {
          materialIdByGeometry.TryGetValue(edge.Dst, out var matId);
          geometry.AddRange(
            DecodeGeometry(bundle, edge.Dst, matId ?? ElementId.InvalidElementId, applyReferencePoint: true)
          );
        }
        if (geometry.Count == 0)
        {
          var reason = _lastDecodeFailure ?? "object did not convert to any native geometry";
          session.RecordObject(appId, srcType, Status.ERROR, reason, sw.ElapsedMilliseconds);
          conversionResults.Add(new(Status.ERROR, source, null, null, new ConversionException(reason), srcType));
          continue;
        }

        var ds = DirectShape.CreateElement(doc, ResolveCategory(doc, props, validCategories, categoryCache));
        SetNameSafe(ds, PropString(props, "name"));
        ds.SetShape(geometry);
        StampMarker(ds, marker);

        bakedObjectIds.Add(ds.UniqueId);
        conversionResults.Add(new(Status.SUCCESS, source, ds.UniqueId, "Direct Shape", null, srcType));
        session.RecordObject(appId, srcType, Status.SUCCESS, null, sw.ElapsedMilliseconds);
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        session.RecordObject(appId, srcType, Status.ERROR, ex.Message, sw.ElapsedMilliseconds);
        conversionResults.Add(new(Status.ERROR, source, null, null, ex, srcType));
      }
    }
  }

  // ── instances ─────────────────────────────────────────────────────────────────────────────────────────
  [SuppressMessage("Maintainability", "CA1506:Avoid excessive class coupling")]
  private void BakeInstances(
    Document doc,
    ArtefactBundle bundle,
    ArtefactRelations rels,
    Dictionary<int, ElementId> materialIdByGeometry,
    Dictionary<string, ElementId> validCategories,
    Dictionary<string, ElementId> categoryCache,
    string marker,
    List<string> bakedObjectIds,
    HashSet<ReceiveConversionResult> conversionResults,
    ArtefactSessionLog session,
    CancellationToken cancellationToken
  )
  {
    var docUnits = _converterSettings.Current.SpeckleUnits;
    var library = DirectShapeLibrary.GetDirectShapeLibrary(doc);

    // definitions (incl. nested blocks/families): a DEFINITION node owns its geometry directly (DEFINES → geometry)
    // and may also contain nested instance placements (DEFINES_INSTANCE → INSTANCE node) — mirrors
    // RhinoHostObjectArtefactBuilder.BuildDefinitions. Nested definitions are built depth-first (memoized in
    // defKeyByNode) so a parent can reference the child via a DirectShape.CreateGeometryInstance member
    // (GeometryInstance derives from GeometryObject, so it slots into the parent's geometry list like any solid/mesh).
    var defKeyByNode = new Dictionary<int, string>();
    var defBuilding = new HashSet<int>();

    string? BuildDefinition(int defNodeK)
    {
      if (defKeyByNode.TryGetValue(defNodeK, out var already))
      {
        return already;
      }
      if (!bundle.Nodes.TryGetValue(defNodeK, out var defNode) || defNode.Kind != NodeKind.Definition)
      {
        return null;
      }
      if (!defBuilding.Add(defNodeK))
      {
        return null; // cycle guard — never stack-overflow on a bad bundle
      }
      session.Increment("definitionsSeen");

      var geometry = new List<GeometryObject>();
      _lastDecodeFailure = null;

      // direct geometry members (DEFINES → geometry blobs). Definition/local-space vertices — no reference-point
      // re-basing here; it's applied once, to the outer instance placement, in the top-level BakeInstances loop
      // below [ENG-9099].
      if (rels.DefinesByDefinition.TryGetValue(defNodeK, out var geomKs))
      {
        foreach (var geomK in geomKs)
        {
          materialIdByGeometry.TryGetValue(geomK, out var matId);
          geometry.AddRange(
            DecodeGeometry(bundle, geomK, matId ?? ElementId.InvalidElementId, applyReferencePoint: false)
          );
        }
      }
      // captured now — a recursive BuildDefinition call below resets _lastDecodeFailure for the child definition.
      var directGeometryFailure = _lastDecodeFailure;

      // nested block/family members (DEFINES_INSTANCE → INSTANCE node): build the child definition first
      // (depth-first) and add a geometry-instance reference to it with the nested placement's own transform.
      // Still local/definition space (relative to the parent definition, not world space) — no reference-point
      // correction here either; only the outermost placement crosses into world/document space.
      if (rels.DefinesInstanceByDefinition.TryGetValue(defNodeK, out var nestedInstNodeKs))
      {
        foreach (var instNodeK in nestedInstNodeKs)
        {
          if (!bundle.Nodes.TryGetValue(instNodeK, out var nestedInst) || nestedInst.DefRef is not int childDefNodeK)
          {
            continue;
          }
          var childDefKey = BuildDefinition(childDefNodeK);
          if (childDefKey is null)
          {
            session.Increment("nestedInstancesUnresolved");
            continue;
          }
          var nestedTransform = BuildInstanceTransform(
            nestedInst.Transform,
            nestedInst.Units is { Length: > 0 } u ? u : docUnits,
            applyReferencePoint: false
          );
          geometry.AddRange(DirectShape.CreateGeometryInstance(doc, childDefKey, nestedTransform));
          session.Increment("nestedInstancesPlaced");
        }
      }

      defBuilding.Remove(defNodeK);

      if (geometry.Count == 0)
      {
        session.Increment("definitionsEmpty");
        if (directGeometryFailure is not null)
        {
          _logger.LogWarning("Definition {DefNodeK} built with no geometry: {Reason}", defNodeK, directGeometryFailure);
        }
        return null;
      }

      var defKey = $"spk-def-{defNodeK.ToString(CultureInfo.InvariantCulture)}";
      library.AddDefinition(defKey, geometry);
      defKeyByNode[defNodeK] = defKey;
      return defKey;
    }

    foreach (var kv in bundle.Nodes)
    {
      if (kv.Value.Kind == NodeKind.Definition)
      {
        BuildDefinition(kv.Key);
      }
    }

    // placements: one DirectShape per DISPLAY_INSTANCE edge (object → INSTANCE node); an object may place several.
    foreach (var edge in rels.DisplayInstanceEdges)
    {
      cancellationToken.ThrowIfCancellationRequested();
      session.Increment("placementsAttempted");
      int objK = edge.Src;
      int instNodeK = edge.Dst;
      if (
        !bundle.Nodes.TryGetValue(instNodeK, out var instNode) || !bundle.ObjectAppIds.TryGetValue(objK, out var appId)
      )
      {
        continue;
      }
      bundle.Properties.TryGetValue(objK, out var props);
      var source = Source(appId);
      var srcType = SrcType(props);
      var sw = Stopwatch.StartNew();
      if (instNode.DefRef is not int defNodeK || !defKeyByNode.TryGetValue(defNodeK, out var defKey))
      {
        session.RecordObject(
          appId,
          srcType,
          Status.ERROR,
          "references a definition with no geometry",
          sw.ElapsedMilliseconds
        );
        conversionResults.Add(
          new(
            Status.ERROR,
            source,
            null,
            null,
            new ConversionException("Instance references a definition with no geometry"),
            srcType
          )
        );
        continue;
      }

      try
      {
        var transform = BuildInstanceTransform(
          instNode.Transform,
          instNode.Units is { Length: > 0 } u ? u : docUnits,
          applyReferencePoint: true
        );
        var geometry = DirectShape.CreateGeometryInstance(doc, defKey, transform);

        var ds = DirectShape.CreateElement(doc, ResolveCategory(doc, props, validCategories, categoryCache));
        SetNameSafe(ds, PropString(props, "name"));
        ds.SetShape(geometry);
        StampMarker(ds, marker);

        bakedObjectIds.Add(ds.UniqueId);
        conversionResults.Add(new(Status.SUCCESS, source, ds.UniqueId, "Instance (Direct Shape)", null, srcType));
        session.RecordObject(appId, srcType, Status.SUCCESS, null, sw.ElapsedMilliseconds);
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        session.RecordObject(appId, srcType, Status.ERROR, ex.Message, sw.ElapsedMilliseconds);
        conversionResults.Add(new(Status.ERROR, source, null, null, ex, srcType));
      }
    }
  }

  // ── instances (as families) ──────────────────────────────────────────────────────────────────────────
  // ENG-9101: bundle-native counterpart of BakeInstances — same DEFINES/DEFINES_INSTANCE/DISPLAY_INSTANCE
  // traversal and the same DecodeGeometry-derived geometry selection, but bakes real Revit families via
  // RevitFamilyBaker instead of DirectShapeLibrary geometry instances. No Base graph, no v1 delegation.
  // Split into BuildFamilyDefinitions + PlaceFamilyInstances (below) to keep each under the complexity budget.
  [SuppressMessage("Maintainability", "CA1506:Avoid excessive class coupling")]
  private void BakeInstancesAsFamilies(
    Document doc,
    ArtefactBundle bundle,
    ArtefactRelations rels,
    Dictionary<int, ElementId> materialIdByNode,
    string marker,
    List<string> bakedObjectIds,
    HashSet<ReceiveConversionResult> conversionResults,
    ArtefactSessionLog session,
    CancellationToken cancellationToken
  )
  {
    var familyMaterialsByNode = BuildFamilyMaterialsByNode(bundle);
    // definitions built depth-first (memoized here) so a parent can nest an already-baked child.
    var symbolByDefNode = new Dictionary<int, FamilySymbol>();
    var defBuilding = new HashSet<int>();

    foreach (var kv in bundle.Nodes)
    {
      if (kv.Value.Kind == NodeKind.Definition)
      {
        BuildFamilyDefinition(doc, bundle, rels, materialIdByNode, familyMaterialsByNode, kv.Key, symbolByDefNode, defBuilding, session);
      }
    }

    PlaceFamilyInstances(doc, bundle, rels, symbolByDefNode, marker, bakedObjectIds, conversionResults, session, cancellationToken);
  }

  // One DEFINITION node → one real Revit family, built depth-first (a nested block/family's definition must exist
  // before its parent can place it). Mirrors BakeInstances' local BuildDefinition, extracted to its own method
  // (rather than a local closure) to keep BakeInstancesAsFamilies' complexity down.
  [SuppressMessage("Maintainability", "CA1506:Avoid excessive class coupling")]
  private FamilySymbol? BuildFamilyDefinition(
    Document doc,
    ArtefactBundle bundle,
    ArtefactRelations rels,
    Dictionary<int, ElementId> materialIdByNode,
    Dictionary<int, RenderMaterial> familyMaterialsByNode,
    int defNodeK,
    Dictionary<int, FamilySymbol> symbolByDefNode,
    HashSet<int> defBuilding,
    ArtefactSessionLog session
  )
  {
    if (symbolByDefNode.TryGetValue(defNodeK, out var already))
    {
      return already;
    }
    if (!bundle.Nodes.TryGetValue(defNodeK, out var defNode) || defNode.Kind != NodeKind.Definition)
    {
      return null;
    }
    if (!defBuilding.Add(defNodeK))
    {
      return null; // cycle guard — never stack-overflow on a bad bundle
    }
    session.Increment("definitionsSeen");

    var docUnits = _converterSettings.Current.SpeckleUnits;

    // direct geometry members (DEFINES → geometry). Definition/local-space — no reference-point re-basing here;
    // it's applied once, to the outer instance placement, in PlaceFamilyInstances [ENG-9099].
    var members = new List<(GeometryObject geometry, int? materialNodeKey)>();
    _lastDecodeFailure = null;
    if (rels.DefinesByDefinition.TryGetValue(defNodeK, out var geomKs))
    {
      foreach (var geomK in geomKs)
      {
        int? matNodeK = rels.MaterialByGeometry.TryGetValue(geomK, out var mk) ? mk : null;
        foreach (var geom in DecodeFamilyGeometry(bundle, geomK))
        {
          members.Add((geom, matNodeK));
        }
      }
    }
    var directGeometryFailure = _lastDecodeFailure;

    // nested block/family members (DEFINES_INSTANCE → INSTANCE node): build the child definition first
    // (depth-first), then place it as a nested family instance inside this definition's family document.
    var nestedPlacements = new List<(string childDefinitionKey, Matrix4x4 transform, string units)>();
    if (rels.DefinesInstanceByDefinition.TryGetValue(defNodeK, out var nestedInstNodeKs))
    {
      foreach (var instNodeK in nestedInstNodeKs)
      {
        if (!bundle.Nodes.TryGetValue(instNodeK, out var nestedInst) || nestedInst.DefRef is not int childDefNodeK)
        {
          continue;
        }
        if (
          BuildFamilyDefinition(doc, bundle, rels, materialIdByNode, familyMaterialsByNode, childDefNodeK, symbolByDefNode, defBuilding, session)
          is null
        )
        {
          session.Increment("nestedInstancesUnresolved");
          continue;
        }
        nestedPlacements.Add(
          (
            DefinitionKey(childDefNodeK),
            ParseInstanceMatrix(nestedInst.Transform),
            nestedInst.Units is { Length: > 0 } u ? u : docUnits
          )
        );
        session.Increment("nestedInstancesPlaced");
      }
    }

    defBuilding.Remove(defNodeK);

    if (members.Count == 0 && nestedPlacements.Count == 0)
    {
      session.Increment("definitionsEmpty");
      if (directGeometryFailure is not null)
      {
        _logger.LogWarning("Definition {DefNodeK} built with no geometry: {Reason}", defNodeK, directGeometryFailure);
      }
      return null;
    }

    var result = _familyBaker.BakeDefinitionFromArtifact(
      doc,
      DefinitionKey(defNodeK),
      defNode.Name,
      ResolveDefinitionCategory(bundle, rels, defNodeK),
      members,
      familyMaterialsByNode,
      materialIdByNode,
      nestedPlacements
    );
    if (result is null)
    {
      return null;
    }
    symbolByDefNode[defNodeK] = result.Value.symbol;
    return result.Value.symbol;
  }

  // One FamilyInstance per DISPLAY_INSTANCE edge (object → INSTANCE node); an object may place several.
  [SuppressMessage("Maintainability", "CA1506:Avoid excessive class coupling")]
  private void PlaceFamilyInstances(
    Document doc,
    ArtefactBundle bundle,
    ArtefactRelations rels,
    Dictionary<int, FamilySymbol> symbolByDefNode,
    string marker,
    List<string> bakedObjectIds,
    HashSet<ReceiveConversionResult> conversionResults,
    ArtefactSessionLog session,
    CancellationToken cancellationToken
  )
  {
    var docUnits = _converterSettings.Current.SpeckleUnits;
    foreach (var edge in rels.DisplayInstanceEdges)
    {
      cancellationToken.ThrowIfCancellationRequested();
      session.Increment("placementsAttempted");
      int objK = edge.Src;
      int instNodeK = edge.Dst;
      if (
        !bundle.Nodes.TryGetValue(instNodeK, out var instNode) || !bundle.ObjectAppIds.TryGetValue(objK, out var appId)
      )
      {
        continue;
      }
      bundle.Properties.TryGetValue(objK, out var props);
      var source = Source(appId);
      var srcType = SrcType(props);
      var sw = Stopwatch.StartNew();
      if (instNode.DefRef is not int defNodeK || !symbolByDefNode.TryGetValue(defNodeK, out var symbol))
      {
        session.RecordObject(
          appId,
          srcType,
          Status.ERROR,
          "references a definition with no geometry",
          sw.ElapsedMilliseconds
        );
        conversionResults.Add(
          new(
            Status.ERROR,
            source,
            null,
            null,
            new ConversionException("Instance references a definition with no geometry"),
            srcType
          )
        );
        continue;
      }

      try
      {
        var transform = ParseInstanceMatrix(instNode.Transform);
        var units = instNode.Units is { Length: > 0 } u ? u : docUnits;
        var instance =
          _familyBaker.PlaceInstanceFromArtifact(
            doc,
            symbol,
            transform,
            units,
            _converterSettings.Current.ReferencePointTransform
          ) ?? throw new ConversionException("Failed to place family instance");
        StampMarker(instance, marker);

        bakedObjectIds.Add(instance.UniqueId);
        conversionResults.Add(new(Status.SUCCESS, source, instance.UniqueId, "FamilyInstance", null, srcType));
        session.RecordObject(appId, srcType, Status.SUCCESS, null, sw.ElapsedMilliseconds);
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        session.RecordObject(appId, srcType, Status.ERROR, ex.Message, sw.ElapsedMilliseconds);
        conversionResults.Add(new(Status.ERROR, source, null, null, ex, srcType));
      }
    }
  }

  private static string DefinitionKey(int defNodeK) =>
    $"artifact-def-{defNodeK.ToString(CultureInfo.InvariantCulture)}";

  // Family-local placeholder materials, one per MATERIAL node — baked into each temp family document during
  // authoring, then overwritten with the real project material (materialIdByNode) once the family is loaded.
  private static Dictionary<int, RenderMaterial> BuildFamilyMaterialsByNode(ArtefactBundle bundle)
  {
    var map = new Dictionary<int, RenderMaterial>();
    foreach (var kv in bundle.Nodes)
    {
      if (kv.Value.Kind != NodeKind.Material)
      {
        continue;
      }
      var n = kv.Value;
      var key = kv.Key.ToString(CultureInfo.InvariantCulture);
      map[kv.Key] = new RenderMaterial
      {
        name = n.Name ?? $"material-{key}",
        diffuse = n.Argb ?? unchecked((int)0xFFFFFFFF),
        opacity = n.Opacity ?? 1.0,
        metalness = n.Metalness ?? 0.0,
        roughness = n.Roughness ?? 1.0,
        applicationId = $"mat-{key}",
      };
    }
    return map;
  }

  // The first object that places this definition determines its Revit category — mirrors ResolveCategory's
  // prop-reading convention. Kept separate because FamilyCategoryUtils needs the raw builtInCategory string up
  // front (before the family document exists), not a resolved ElementId.
  private static string? ResolveDefinitionCategory(ArtefactBundle bundle, ArtefactRelations rels, int defNodeK)
  {
    foreach (var edge in rels.DisplayInstanceEdges)
    {
      if (!bundle.Nodes.TryGetValue(edge.Dst, out var instNode) || instNode.DefRef != defNodeK)
      {
        continue;
      }
      bundle.Properties.TryGetValue(edge.Src, out var props);
      return PropString(props, "builtInCategory") ?? PropString(props, "category");
    }
    return null;
  }

  // Family geometry needs a Solid for FreeFormElement, unlike BakeAtomic/BakeInstances' Mesh-targeted BuildMesh —
  // reuses the same freeform mesh converter the v1 family pipeline already relies on for that. Always local space
  // (no applyReferencePoint — a definition's own shape never gets the outer reference-point correction).
  private List<GeometryObject> DecodeFamilyGeometry(ArtefactBundle bundle, int geomK)
  {
    if (!bundle.Geometries.TryGetValue(geomK, out var g) || !g.IsSgeo)
    {
      return [];
    }
    if (SgeoDecoder.TryDecodeMesh(g.Content, out var sm))
    {
      try
      {
        var mesh = new SMesh { vertices = sm.Vertices.ToList(), faces = sm.Faces.ToList(), units = sm.Units };
        return [_freeformMeshConverter.Convert(mesh)];
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        _lastDecodeFailure = $"geom {geomK} (SGEO mesh) freeform convert failed — {ex.GetType().Name}: {ex.Message}";
        return [];
      }
    }

    Base? decoded = null;
    try
    {
      decoded = SgeoDecoder.Decode(g.Content);
      using var referencePointSuppression = _converterSettings.Push(s => s with { ReferencePointTransform = null });
      return _geometryConverter.Convert(decoded);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      string stage = decoded is null ? "decode" : $"convert of {decoded.speckle_type}";
      _lastDecodeFailure = $"geom {geomK} ({g.Type}) {stage} failed — {ex.GetType().Name}: {ex.Message}";
      return [];
    }
  }

  private static Matrix4x4 ParseInstanceMatrix(string? csv)
  {
    var d = ParseMatrix(csv);
    return new Matrix4x4(
      d[0],
      d[1],
      d[2],
      d[3],
      d[4],
      d[5],
      d[6],
      d[7],
      d[8],
      d[9],
      d[10],
      d[11],
      d[12],
      d[13],
      d[14],
      d[15]
    );
  }

  // ── camera views ──────────────────────────────────────────────────────────────────────────────────────
  // One View3D per envelope.camera_views.parquet row. A bad camera reports a per-row error (mirrors BakeAtomic)
  // without affecting the rest of the receive.
  private void BakeCameraViews(
    ArtefactBundle bundle,
    string marker,
    List<string> bakedObjectIds,
    HashSet<ReceiveConversionResult> conversionResults,
    ArtefactSessionLog session
  )
  {
    foreach (var cameraView in bundle.CameraViews)
    {
      var appId = $"camera-view-{cameraView.View.ToString(CultureInfo.InvariantCulture)}";
      var source = Source(appId);
      var sw = Stopwatch.StartNew();
      try
      {
        var name = cameraView.Name?.Trim();
        if (name is not { Length: > 0 })
        {
          throw new ConversionException("Camera view has no name");
        }

        var forward = new XYZ(cameraView.ForwardX, cameraView.ForwardY, cameraView.ForwardZ);
        var up = new XYZ(cameraView.UpX, cameraView.UpY, cameraView.UpZ);
        if (forward.IsZeroLength() || up.IsZeroLength())
        {
          throw new ConversionException("Camera view has a zero-length forward or up direction");
        }

        var units = cameraView.Units is { Length: > 0 } u ? u : bundle.Units;
        var fTypeId = ResolveForge(units);
        var eye = _referencePointConverter.ConvertToInternalCoordinates(
          new XYZ(
            _scalingService.ScaleToNative(cameraView.PosX, fTypeId),
            _scalingService.ScaleToNative(cameraView.PosY, fTypeId),
            _scalingService.ScaleToNative(cameraView.PosZ, fTypeId)
          ),
          true
        );
        forward = _referencePointConverter.ConvertToInternalCoordinates(forward, false).Normalize();
        up = _referencePointConverter.ConvertToInternalCoordinates(up, false).Normalize();

        var uniqueId = _viewBaker.BakeArtefactView(name, cameraView.IsOrtho, eye, forward, up, marker);

        bakedObjectIds.Add(uniqueId);
        conversionResults.Add(new(Status.SUCCESS, source, uniqueId, "View3D", null, "Camera View"));
        session.RecordObject(appId, "Camera View", Status.SUCCESS, null, sw.ElapsedMilliseconds);
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        session.RecordObject(appId, "Camera View", Status.ERROR, ex.Message, sw.ElapsedMilliseconds);
        conversionResults.Add(new(Status.ERROR, source, null, null, ex, "Camera View"));
      }
    }
  }

  // ── geometry ──────────────────────────────────────────────────────────────────────────────────────────
  // Decodes one SGEO geometry index → Revit GeometryObjects. Meshes take the fast hand-rolled path
  // (TessellatedShapeBuilder, material baked per face). Points, curves, regions and other non-mesh primitives decode
  // to a Speckle geometry object and convert via the shared Revit ToHost geometry converter (mirrors
  // Rhino/AutoCAD's SGEO fallback — see RhinoHostObjectArtefactBuilder.DecodeGeometryIndex). An unsupported
  // primitive degrades to nothing (with a recorded reason, see _lastDecodeFailure) rather than aborting the receive.
  //
  // applyReferencePoint distinguishes WORLD-space vertices (atomic DISPLAY geometry — true, needs re-basing) from
  // LOCAL/definition-space vertices (an instance/family's own shape — false); see FormatReferencePointTransform on
  // the send side and BuildInstanceTransform below [ENG-9099]. The mesh path takes this explicitly (ToInternalPoints);
  // the shared ToHost geometry converter used for the non-mesh fallback has no such parameter and always reads the
  // AMBIENT reference-point transform, so it's suppressed via a temporary settings push when applyReferencePoint is
  // false, to keep both paths consistent — otherwise a non-mesh definition shape would get double-corrected.
  private List<GeometryObject> DecodeGeometry(
    ArtefactBundle bundle,
    int geomK,
    ElementId materialId,
    bool applyReferencePoint
  )
  {
    if (!bundle.Geometries.TryGetValue(geomK, out var g) || !g.IsSgeo)
    {
      return new List<GeometryObject>();
    }
    if (SgeoDecoder.TryDecodeMesh(g.Content, out var sm))
    {
      return BuildMesh(sm, materialId, applyReferencePoint);
    }

    Base? decoded = null;
    try
    {
      decoded = SgeoDecoder.Decode(g.Content);
      if (applyReferencePoint)
      {
        return _geometryConverter.Convert(decoded);
      }
      using var referencePointSuppression = _converterSettings.Push(s => s with { ReferencePointTransform = null });
      return _geometryConverter.Convert(decoded);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      string stage = decoded is null ? "decode" : $"convert of {decoded.speckle_type}";
      _lastDecodeFailure = $"geom {geomK} ({g.Type}) {stage} failed — {ex.GetType().Name}: {ex.Message}";
      _logger.LogWarning(
        ex,
        "Skipped SGEO geometry {GeomK} ({Bytes} bytes) at {Stage}: {Error}",
        geomK,
        g.Content.Length,
        stage,
        ex.Message
      );
      return new List<GeometryObject>();
    }
  }

  // Flat SGEO mesh (verts/faces, Speckle count-prefixed) → Revit GeometryObjects. Mirrors MeshConverterToHost: scale
  // to internal units + apply the reference-point transform, fan/triangulate, salvage fallback.
  private List<GeometryObject> BuildMesh(SgeoMesh sm, ElementId materialId, bool applyReferencePoint)
  {
    using var tsb = new TessellatedShapeBuilder();
    tsb.Fallback = TessellatedShapeBuilderFallback.Salvage;
    tsb.Target = TessellatedShapeBuilderTarget.Mesh;
    tsb.GraphicsStyleId = ElementId.InvalidElementId;
    tsb.OpenConnectedFaceSet(false);

    var verts = ToInternalPoints(sm.Vertices, sm.Units, applyReferencePoint);
    var f = sm.Faces;
    int p = 0;
    while (p < f.Length)
    {
      int n = f[p];
      if (n < 3)
      {
        n += 3; // legacy 0 -> triangle, 1 -> quad
      }
      if (n < 3 || p + n >= f.Length)
      {
        break;
      }

      var points = new List<XYZ>(n);
      bool ok = true;
      for (int k = 1; k <= n; k++)
      {
        int vi = f[p + k];
        if (vi < 0 || vi >= verts.Length)
        {
          ok = false;
          break;
        }
        points.Add(verts[vi]);
      }

      if (ok)
      {
        if (n == 4 && IsNonPlanarQuad(points))
        {
          // Non-planar quad → two triangles (preferable to TessellatedShapeBuilder forcing it planar).
          tsb.AddFace(new TessellatedFace(new List<XYZ> { points[0], points[1], points[3] }, materialId));
          tsb.AddFace(new TessellatedFace(new List<XYZ> { points[1], points[2], points[3] }, materialId));
        }
        else
        {
          tsb.AddFace(new TessellatedFace(points, materialId));
        }
      }
      p += n + 1;
    }

    tsb.CloseConnectedFaceSet();
    tsb.Build();
    return tsb.GetBuildResult().GetGeometricalObjects().ToList();
  }

  // SGEO flat verts (double[] xyz) → Revit internal-unit XYZ[]. Reference-point transform applied only for
  // world-space (atomic) vertices — see DecodeGeometry's applyReferencePoint doc.
  private XYZ[] ToInternalPoints(double[] verts, string units, bool applyReferencePoint)
  {
    var fTypeId = ResolveForge(units);
    var points = new XYZ[verts.Length / 3];
    for (int i = 0, k = 0; i + 2 < verts.Length; i += 3)
    {
      var x = _scalingService.ScaleToNative(verts[i], fTypeId);
      var y = _scalingService.ScaleToNative(verts[i + 1], fTypeId);
      var z = _scalingService.ScaleToNative(verts[i + 2], fTypeId);
      points[k++] = applyReferencePoint
        ? _referencePointConverter.ConvertToInternalCoordinates(new XYZ(x, y, z), true)
        : new XYZ(x, y, z);
    }
    return points;
  }

  private ForgeTypeId ResolveForge(string units)
  {
    try
    {
      return _scalingService.UnitsToNative(units);
    }
    catch (UnitNotSupportedException)
    {
      return UnitTypeId.Meters;
    }
  }

  private static bool IsNonPlanarQuad(IReadOnlyList<XYZ> p)
  {
    if (p.Count != 4)
    {
      return false;
    }
    // Volume of the tetrahedron formed by the 4 corners; non-zero ⇒ non-planar.
    var v1 = p[1] - p[0];
    var v2 = p[2] - p[0];
    var v3 = p[3] - p[0];
    return Math.Abs(v1.CrossProduct(v2).DotProduct(v3)) > 1e-9;
  }

  // ── materials ─────────────────────────────────────────────────────────────────────────────────────────
  private Dictionary<int, ElementId> CreateMaterials(Document doc, ArtefactBundle bundle)
  {
    var idByNode = new Dictionary<int, ElementId>();
    // Dedup on name + the full colour signature. Name alone is not enough (older bundles carry no name, so every
    // material would collapse into one all-grey element); colour alone is not enough either, since two distinctly
    // named materials may share a diffuse and must stay separate Revit elements.
    var byKey = new Dictionary<string, ElementId>(StringComparer.Ordinal);
    foreach (var kv in bundle.Nodes)
    {
      var node = kv.Value;
      if (node.Kind != NodeKind.Material)
      {
        continue;
      }
      try
      {
        var key = MaterialKey(node);
        if (!byKey.TryGetValue(key, out var id))
        {
          id = FindOrCreateMaterial(doc, node);
          byKey[key] = id;
        }
        idByNode[kv.Key] = id;
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogError(ex, "Failed to create artefact material for node {Node}", kv.Key);
      }
    }
    return idByNode;
  }

  private static string MaterialKey(ArtefactNode node) =>
    string.Format(
      CultureInfo.InvariantCulture,
      "{0}|{1}|{2}|{3}|{4}",
      node.Name ?? string.Empty,
      node.Argb ?? -1,
      node.Opacity ?? 1.0,
      node.Metalness ?? 0.0,
      node.Roughness ?? 1.0
    );

  private ElementId FindOrCreateMaterial(Document doc, ArtefactNode node)
  {
    int argb = node.Argb ?? unchecked((int)0xFFFFFFFF);
    double opacity = Clamp01(node.Opacity ?? 1.0);
    double metalness = Clamp01(node.Metalness ?? 0.0);
    double roughness = Clamp01(node.Roughness ?? 1.0);

    // Prefer the authored material name (Rhino/Revit/AutoCAD material table entry); fall back to a colour-derived
    // label for bundles produced before names were carried, so each colour still gets its own Revit material.
    var label = node.Name is { Length: > 0 } n ? n : $"Speckle {(uint)argb:X8}";
    if (opacity < 0.999)
    {
      label += $" t{(int)((1 - opacity) * 100)}";
    }
    var name = _revitUtils.RemoveInvalidChars(label);

    // A Revit material is identified by name, so re-receives reuse the existing element — but only when its colour
    // still matches. Two source materials can legitimately share a name with different colours (Rhino allows it);
    // suffixing the ARGB keeps the second one from silently inheriting the first one's appearance. Names are only
    // authoritative now that producers carry them; the colour-derived fallback label was already unique per colour.
    var revitColor = new Color((byte)((argb >> 16) & 0xFF), (byte)((argb >> 8) & 0xFF), (byte)(argb & 0xFF));
    var existing = FindMaterialByName(doc, name);
    if (existing is not null && !SameColor(existing.Color, revitColor))
    {
      name = _revitUtils.RemoveInvalidChars($"{label} {(uint)argb:X8}");
      existing = FindMaterialByName(doc, name);
    }
    if (existing is not null)
    {
      return existing.Id;
    }

    var id = Material.Create(doc, name);
    var material = (Material)doc.GetElement(id);
    material.Color = revitColor;
    material.Transparency = (int)((1 - opacity) * 100);
    material.Shininess = (int)(metalness * 128);
    material.Smoothness = (int)((1 - roughness) * 100);
    return id;
  }

  private static Material? FindMaterialByName(Document doc, string name)
  {
    using var collector = new FilteredElementCollector(doc);
    return collector
      .OfClass(typeof(Material))
      .Cast<Material>()
      .FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
  }

  // An existing material with no valid colour counts as a match — there is nothing to compare against, and minting a
  // suffixed duplicate on every receive would be worse than reusing it.
  private static bool SameColor(Color existing, Color wanted) =>
    !existing.IsValid || (existing.Red == wanted.Red && existing.Green == wanted.Green && existing.Blue == wanted.Blue);

  private static double Clamp01(double v) =>
    v < 0 ? 0
    : v > 1 ? 1
    : v;

  // ── category ──────────────────────────────────────────────────────────────────────────────────────────
  // Top-level valid DirectShape categories by display name, built once per receive.
  private static Dictionary<string, ElementId> BuildValidDirectShapeCategories(Document doc)
  {
    var map = new Dictionary<string, ElementId>(StringComparer.OrdinalIgnoreCase);
    foreach (Category c in doc.Settings.Categories)
    {
      if (!map.ContainsKey(c.Name) && DirectShape.IsValidCategoryId(c.Id, doc))
      {
        map[c.Name] = c.Id;
      }
    }
    return map;
  }

  // Resolves a DirectShape category for an object: prefer the OST_* builtInCategory enum (locale-independent), then
  // the localized category display name, else Generic Models.
  private static ElementId ResolveCategory(
    Document doc,
    Dictionary<string, object?>? props,
    Dictionary<string, ElementId> validCategories,
    Dictionary<string, ElementId> cache
  )
  {
    var bic = PropString(props, "builtInCategory");
    var display = PropString(props, "category");
    var key = bic ?? display ?? "";
    if (cache.TryGetValue(key, out var cached))
    {
      return cached;
    }

    ElementId resolved = new(BuiltInCategory.OST_GenericModel);
    if (bic is not null && Enum.TryParse(bic, out BuiltInCategory cat))
    {
      var c = Category.GetCategory(doc, cat);
      if (c is not null && DirectShape.IsValidCategoryId(c.Id, doc))
      {
        resolved = new ElementId(cat);
      }
    }
    else if (display is not null && validCategories.TryGetValue(display, out var byName))
    {
      resolved = byName;
    }

    cache[key] = resolved;
    return resolved;
  }

  // ENG-8947 / ENG-9099: rebuild the sender's reference-point transform from the bundle meta offset. Two formats
  // share the one field: a 3-value "x,y,z" CSV (projectBasePoint / surveyPoint — translation only, no rotation to
  // lose) or a 16-value row-major CSV (sharedCoordinates — full rotation + translation, same layout ParseMatrix
  // already uses for InstanceProxy transforms). Both are in the bundle's display units; scale to internal feet so
  // the result composes with the receive setting and applies through ConvertToInternalCoordinates. Null (internal
  // origin / fallback) → no source re-basing.
  private Transform? ReadSourceReferencePointTransform(ArtefactBundle bundle)
  {
    if (bundle.ReferencePointOffset is not { Length: > 0 } offsetCsv)
    {
      return null;
    }
    var parts = offsetCsv.Split(',');
    var fTypeId = ResolveForge(bundle.Units);

    if (parts.Length == 16)
    {
      var d = new double[16];
      for (int i = 0; i < 16; i++)
      {
        if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out d[i]))
        {
          return null;
        }
      }
      var full = Transform.Identity;
      full.BasisX = new XYZ(d[0], d[4], d[8]);
      full.BasisY = new XYZ(d[1], d[5], d[9]);
      full.BasisZ = new XYZ(d[2], d[6], d[10]);
      full.Origin = new XYZ(
        _scalingService.ScaleToNative(d[3], fTypeId),
        _scalingService.ScaleToNative(d[7], fTypeId),
        _scalingService.ScaleToNative(d[11], fTypeId)
      );
      return full;
    }

    if (parts.Length != 3)
    {
      return null;
    }
    var o = new double[3];
    for (int i = 0; i < 3; i++)
    {
      if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out o[i]))
      {
        return null;
      }
    }
    // Identity + Origin (not Transform.CreateTranslation) to match ReferencePointHelper.GetTransformFromRootObject and
    // keep the analyzer's disposable-ownership tracking happy — the result is pushed into converter settings.
    var t = Transform.Identity;
    t.Origin = new XYZ(
      _scalingService.ScaleToNative(o[0], fTypeId),
      _scalingService.ScaleToNative(o[1], fTypeId),
      _scalingService.ScaleToNative(o[2], fTypeId)
    );
    return t;
  }

  // ── transforms ────────────────────────────────────────────────────────────────────────────────────────
  // Instance transform CSV (row-major 16) → Revit Transform: basis columns carry rotation/scale (unscaled), only the
  // translation column is converted to internal units.
  //
  // applyReferencePoint [ENG-9099]: the sender bakes (reference point)⁻¹ into every instance's placement transform
  // (definition geometry itself stays in local/family space, see DecodeGeometry). To land back in the receiver's
  // internal coordinates, the CURRENT effective reference-point transform (receiver setting ∘ sender's recorded
  // transform, pushed onto _converterSettings before this runs — see BakeAll) must be composed onto the OUTERMOST
  // placement only: newTransform.OfPoint(p) = referencePoint.OfPoint(rawTransform.OfPoint(p)), i.e.
  // referencePoint.Multiply(rawTransform). Nested placements inside a definition (BuildDefinition's
  // DEFINES_INSTANCE loop) stay purely local/relative and must NOT get this correction — pass false there.
  private Transform BuildInstanceTransform(string? csv, string units, bool applyReferencePoint)
  {
    var d = ParseMatrix(csv);
    double w = Math.Abs(d[15]) < 1e-12 ? 1.0 : d[15];
    var t = Transform.Identity;
    t.BasisX = new XYZ(d[0], d[4], d[8]);
    t.BasisY = new XYZ(d[1], d[5], d[9]);
    t.BasisZ = new XYZ(d[2], d[6], d[10]);
    t.Origin = new XYZ(
      _scalingService.ScaleToNative(d[3] / w, units),
      _scalingService.ScaleToNative(d[7] / w, units),
      _scalingService.ScaleToNative(d[11] / w, units)
    );

    if (applyReferencePoint && _converterSettings.Current.ReferencePointTransform is { } referencePoint)
    {
      return referencePoint.Multiply(t);
    }
    return t;
  }

  private static double[] ParseMatrix(string? csv)
  {
    var d = new double[16];
    if (csv is { Length: > 0 } text)
    {
      var parts = text.Split(',');
      for (int i = 0; i < 16 && i < parts.Length; i++)
      {
        double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out d[i]);
      }
    }
    else
    {
      d[0] = d[5] = d[10] = d[15] = 1.0;
    }
    return d;
  }

  // ── clean ─────────────────────────────────────────────────────────────────────────────────────────────
  private void PreClean(Document doc, string marker)
  {
    DirectShapeLibrary.GetDirectShapeLibrary(doc).Reset();
    // A previous receive of this model may predate ENG-9101, when the family setting delegated the whole receive to
    // the v1 builder and left a pinned top-level Group of real families — not tracked by the Comments marker below.
    _groupBaker.PurgeGroups(marker);
    // Same marker convention, but View3D isn't a DirectShape, so it's not covered by the collector loop below either.
    _viewBaker.PurgeArtefactViews(marker);
    var toDelete = new List<ElementId>();
    // DirectShape (BakeAtomic/BakeInstances) and FamilyInstance (BakeInstancesAsFamilies) both stamp the same
    // Comments marker, so cleaning up both covers whichever setting the prior receive used.
    CollectMarked(doc, typeof(DirectShape), marker, toDelete);
    CollectMarked(doc, typeof(FamilyInstance), marker, toDelete);
    if (toDelete.Count > 0)
    {
      doc.Delete(toDelete);
    }
  }

  private static void CollectMarked(Document doc, Type elementClass, string marker, List<ElementId> toDelete)
  {
    using var collector = new FilteredElementCollector(doc);
    foreach (var element in collector.OfClass(elementClass))
    {
      if (
        element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() is string c
        && string.Equals(c, marker, StringComparison.Ordinal)
      )
      {
        toDelete.Add(element.Id);
      }
    }
  }

  // ── helpers ───────────────────────────────────────────────────────────────────────────────────────────
  // Element, not DirectShape, so the same stamp works for FamilyInstances baked by BakeInstancesAsFamilies.
  private static void StampMarker(Element element, string marker) =>
    element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set(marker);

  private void SetNameSafe(DirectShape ds, string? name)
  {
    if (name is not { Length: > 0 })
    {
      return;
    }
    try
    {
      ds.SetName(_revitUtils.RemoveInvalidChars(name));
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogDebug(ex, "Could not set DirectShape name '{Name}'", name);
    }
  }

  private static string? PropString(Dictionary<string, object?>? props, string key) =>
    props is not null && props.TryGetValue(key, out var v) && v is string s && s.Length > 0 ? s : null;

  // The object's real Speckle type for the conversion report (so it shows e.g. "Objects.Data.RevitObject > Direct
  // Shape" instead of "Base > …" — the report's Base source is only a UI placeholder, not reconstructed data).
  private static string SrcType(Dictionary<string, object?>? props) =>
    PropString(props, "speckle_type") ?? "Speckle Object";

  /// <summary>Minimal plain <see cref="Base"/> used only as the <c>source</c> of a conversion report entry (the
  /// TypeLoader only accepts assembly-scanned registered types — never a custom subclass).</summary>
  private static Base Source(string appId) => new() { applicationId = appId, id = appId };
}
