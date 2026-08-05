using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Autodesk.Revit.DB;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Diagnostics;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.Revit.HostApp;
using Speckle.Converters.Common;
using Speckle.Converters.RevitShared;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Services;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Objects.Utils;
using Speckle.Sdk;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models;
using Speckle.Sdk.Pipelines;
using Speckle.Sdk.Pipelines.Progress;
using Speckle.Sdk.Pipelines.Receive.Artifacts;

namespace Speckle.Connectors.Revit.Operations.Receive;

/// <summary>
/// Bakes a Speckle 4.0 artefact <see cref="ArtefactBundle"/> <b>directly</b> into the Revit document as native
/// <see cref="DirectShape"/>s — talking only to the neutral dense-int graph + raw Revit API, skipping the v1
/// <c>RootObjectUnpacker</c> / traversal / per-type converter dispatch. SGEO meshes are tessellated straight into
/// DirectShape geometry (mirroring <c>MeshConverterToHost</c>), materials are created directly from MATERIAL nodes,
/// and instances are placed per <c>DISPLAY_INSTANCE</c> edge via the <see cref="DirectShapeLibrary"/>. The
/// receive-side twin of the send-side <c>RevitArtifactRootObjectBuilder</c> — except when
/// <c>ReceiveInstancesAsFamilies</c> is on (see remarks).
/// </summary>
/// <remarks>
/// <para><b>Grouping.</b> Revit has no layers; each baked element is stamped with its model marker in the Comments
/// parameter so a re-receive can collect-and-delete the prior bake cheaply (a deliberate choice to avoid the slow v1
/// Revit-Group baking). Element category comes from the object's <c>category</c>/<c>builtInCategory</c> property.</para>
/// <para><b>ReceiveInstancesAsFamilies.</b> The dense-int graph has no notion of families — <see cref="RevitFamilyBaker"/>
/// needs <c>IInstanceComponent</c>s and a <c>TraversalContext</c> lookup built from a <c>Base</c> graph, so it
/// can't consume bundle data directly. When the setting is on, this builder instead reconstructs a <c>Base</c> graph
/// (<see cref="IArtifactReceiver.Reconstruct"/>) and delegates the whole receive to the v1
/// <see cref="RevitHostObjectBuilder"/>, which already honours it — slower than the direct bake, but the only way to
/// get real families across all Revit versions.</para>
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
  private readonly IArtifactReceiver _artifactReceiver;
  private readonly IHostObjectBuilder _hostObjectBuilder;
  private readonly RevitGroupBaker _groupBaker;

  public RevitHostObjectArtefactBuilder(
    IConverterSettingsStore<RevitConversionSettings> converterSettings,
    IThreadContext threadContext,
    ITransactionManager transactionManager,
    ScalingServiceToHost scalingService,
    IReferencePointConverter referencePointConverter,
    RevitUtils revitUtils,
    ISdkActivityFactory activityFactory,
    ILogger<RevitHostObjectArtefactBuilder> logger,
    IArtifactReceiver artifactReceiver,
    IHostObjectBuilder hostObjectBuilder,
    RevitGroupBaker groupBaker
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
    _artifactReceiver = artifactReceiver;
    _hostObjectBuilder = hostObjectBuilder;
    _groupBaker = groupBaker;
  }

  public async Task<HostObjectBuilderResult> Build(
    ArtefactBundle bundle,
    string projectName,
    string modelName,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    if (_converterSettings.Current.ReceiveInstancesAsFamilies)
    {
      // No bundle-native family baking yet (see remarks) — reconstruct a Base graph off the main thread (mirrors
      // ReceiveOperation's own reconstruction branch) and hand the whole receive to the v1 builder.
      var rootObject = await _threadContext
        .RunOnWorkerAsync(() => Task.FromResult(_artifactReceiver.Reconstruct(bundle, cancellationToken)))
        .ConfigureAwait(false);
      return await _hostObjectBuilder
        .Build(rootObject, projectName, modelName, onOperationProgressed, cancellationToken)
        .ConfigureAwait(false);
    }

    // Revit API is main-thread-affine and mutations require a transaction → everything runs on the main thread.
    return await _threadContext
      .RunOnMain(() => BakeAll(bundle, projectName, modelName, onOperationProgressed, cancellationToken))
      .ConfigureAwait(false);
  }

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

      // 3 — instances: build definitions from DEFINES → geometry, place one per DISPLAY_INSTANCE edge.
      if (rels.DisplayInstanceEdges.Count > 0)
      {
        onOperationProgressed.Report(new("Converting instances", null));
        using (session.Phase("Instances"))
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
          session.RecordObject(
            appId,
            srcType,
            Status.ERROR,
            "did not convert to any native geometry",
            sw.ElapsedMilliseconds
          );
          conversionResults.Add(
            new(
              Status.ERROR,
              source,
              null,
              null,
              new ConversionException("Object did not convert to any native geometry"),
              srcType
            )
          );
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

  // ── geometry ──────────────────────────────────────────────────────────────────────────────────────────
  // Decodes one SGEO mesh geometry index → Revit GeometryObjects (TessellatedShapeBuilder), material baked per face.
  // Revit receives meshes only (no raw 3dm); non-SGEO / undecodable blobs yield nothing.
  //
  // applyReferencePoint distinguishes WORLD-space vertices (atomic DISPLAY geometry — true, needs re-basing) from
  // LOCAL/definition-space vertices (an instance/family's own shape — false). The sender never bakes the reference
  // point into definition geometry (only into each instance's placement transform, see FormatReferencePointTransform
  // on the send side), so re-applying it here to definition vertices would double up with the correction now applied
  // to the instance placement in BuildInstanceTransform below [ENG-9099].
  private List<GeometryObject> DecodeGeometry(
    ArtefactBundle bundle,
    int geomK,
    ElementId materialId,
    bool applyReferencePoint
  )
  {
    if (
      !bundle.Geometries.TryGetValue(geomK, out var g)
      || !g.IsSgeo
      || !SgeoDecoder.TryDecodeMesh(g.Content, out var sm)
    )
    {
      return new List<GeometryObject>();
    }
    return BuildMesh(sm, materialId, applyReferencePoint);
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
    // A previous receive of this model may have gone through the family-baking fallback (setting was on) and left a
    // pinned top-level Group of real families — not tracked by the Comments marker below, so purge it here too.
    _groupBaker.PurgeGroups(marker);
    var toDelete = new List<ElementId>();
    using (var collector = new FilteredElementCollector(doc))
    {
      foreach (var element in collector.OfClass(typeof(DirectShape)))
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
    if (toDelete.Count > 0)
    {
      doc.Delete(toDelete);
    }
  }

  // ── helpers ───────────────────────────────────────────────────────────────────────────────────────────
  private static void StampMarker(DirectShape ds, string marker) =>
    ds.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set(marker);

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
