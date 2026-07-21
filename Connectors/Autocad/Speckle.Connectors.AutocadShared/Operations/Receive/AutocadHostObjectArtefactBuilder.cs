using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Autocad.HostApp;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Diagnostics;
using Speckle.Connectors.Common.Threading;
using Speckle.Converters.Autocad;
using Speckle.Converters.Common;
using Speckle.Objects.Other;
using Speckle.Objects.Utils;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models;
using Speckle.Sdk.Pipelines;
using Speckle.Sdk.Pipelines.Progress;
using Speckle.Sdk.Pipelines.Receive.Artifacts;
using AcadColor = Autodesk.AutoCAD.Colors.Color;
using AcadEntity = Autodesk.AutoCAD.DatabaseServices.Entity;
using AcadMaterial = Autodesk.AutoCAD.DatabaseServices.Material;
#if NETFRAMEWORK
using System.IO; // net8+ provides this via ImplicitUsings; net48 needs it explicitly.
#endif

namespace Speckle.Connectors.Autocad.Operations.Receive;

/// <summary>
/// Bakes a Speckle 4.0 artefact <see cref="ArtefactBundle"/> <b>directly</b> into the AutoCAD document (also used by
/// Civil3D), talking only to the neutral dense-int graph + raw AutoCAD API — no v1 <c>Base</c>/<c>DataObject</c>/
/// <c>Collection</c>/proxy types and no traversal / <c>AutocadLayerBaker</c> / <c>AutocadMaterialBaker</c> machinery.
/// Solids come from raw ACIS-SAT blobs (<see cref="Body.AcisIn(string)"/>), meshes from SGEO
/// (<see cref="SgeoDecoder.TryDecodeMesh(ReadOnlySpan{byte}, out SgeoMesh)"/>) built straight into a <see cref="PolyFaceMesh"/>; every other SGEO
/// primitive (curves, points, text, regions…) decodes to its Speckle geometry object and converts via the AutoCAD
/// ToHost converter. Layers are the flat AutoCAD layer namespace projected from the scene view (with the source layer
/// colour); materials from MATERIAL nodes (HAS_MATERIAL), by-object colours from COLOR nodes (HAS_COLOR). Instances
/// follow the host-agnostic model: a DEFINITION node owns its geometry directly (DEFINES → geometry, member-grouped,
/// SAT preferred over its display shadow) plus nested placements (DEFINES_INSTANCE) built depth-first as nested
/// <see cref="BlockReference"/>s, and each DISPLAY_INSTANCE edge places the definition into model space. The
/// receive-side twin of the send-side <c>AutocadArtifactRootObjectBuilder</c>.
/// </summary>
[SuppressMessage(
  "Maintainability",
  "CA1506:Avoid excessive class coupling",
  Justification = "A Base-free native bake necessarily touches many AutoCAD DB types (entities, layers, materials, blocks)."
)]
public class AutocadHostObjectArtefactBuilder : IArtifactHostObjectBuilder
{
  private readonly IConverterSettingsStore<AutocadConversionSettings> _converterSettings;
  private readonly IRootToHostConverter _converter;
  private readonly IThreadContext _threadContext;
  private readonly AutocadContext _autocadContext;
  private readonly ISdkActivityFactory _activityFactory;
  private readonly ILogger<AutocadHostObjectArtefactBuilder> _logger;

  public AutocadHostObjectArtefactBuilder(
    IConverterSettingsStore<AutocadConversionSettings> converterSettings,
    IRootToHostConverter converter,
    IThreadContext threadContext,
    AutocadContext autocadContext,
    ISdkActivityFactory activityFactory,
    ILogger<AutocadHostObjectArtefactBuilder> logger
  )
  {
    _converterSettings = converterSettings;
    _converter = converter;
    _threadContext = threadContext;
    _autocadContext = autocadContext;
    _activityFactory = activityFactory;
    _logger = logger;
  }

  public Task<HostObjectBuilderResult> Build(
    ArtefactBundle bundle,
    string projectName,
    string modelName,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  ) =>
    _threadContext.RunOnMain(() => BakeAll(bundle, projectName, modelName, onOperationProgressed, cancellationToken));

#pragma warning disable CA1506
  private HostObjectBuilderResult BakeAll(
#pragma warning restore CA1506
    ArtefactBundle bundle,
    string projectName,
    string modelName,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    var baseLayerName = _autocadContext.RemoveInvalidChars($"Project {projectName}: Model {modelName}");
    using var activity = _activityFactory.Start("Build (artefact)");
    using var session = ArtefactSessionLog.Start(
      "Autocad",
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
    var db = doc.Database;
    var docUnits = _converterSettings.Current.SpeckleUnits;
    var rels = bundle.Relations;
    var objByGeom = rels.ObjectByGeometry();
    var bakedObjectIds = new HashSet<string>();
    var conversionResults = new HashSet<ReceiveConversionResult>();
    var layerCache = new HashSet<string>(StringComparer.Ordinal);
    // objK → its baked entity ids, tracked only for grouped objects (IN_GROUP) so step 4 can rebuild native groups.
    var bakedIdsByObjK = new Dictionary<int, List<ObjectId>>();

    using var docLock = doc.LockDocument();

    // 0 - clean previous receive of this model (entities on this model's layers + its block definitions).
    PreClean(db, baseLayerName);
    PreCleanAdditional(baseLayerName);

    // Transaction discipline: each phase/object gets its OWN short-lived transaction, started on the DOCUMENT
    // TransactionManager, committed immediately. BOTH halves are load-bearing — three other variants were tried
    // on AutoCAD 2023 and fail:
    // - Database.TransactionManager (single OR per-object): the stock ToHost converters used by the SGEO fallback
    //   resolve Document.TransactionManager.TopTransaction, which is a SEPARATE stack that does not surface db-TM
    //   transactions — every spline/hatch conversion throws CheckTopTransaction (67 objects lost, silently);
    // - one receive-wide Document.TransactionManager transaction: converters work, but the final Commit of a
    //   250+-entity AppTransaction hangs indefinitely in the modeless DUI3 context.
    // Per-object doc-TM commits are cheap undebugged (~ms each; the "slow" runs were Rider breaking on first-chance
    // exceptions); they isolate failures and keep every hatch evaluation small (see the Associative=false guard in
    // DecodeAndAppend for the other half of the hang).

    // 1 - materials (MATERIAL nodes) into the document material dictionary; map geometry/object → material id.
    Dictionary<int, ObjectId> materialIdByNode;
    using (session.Phase("Materials"))
    using (var mtr = doc.TransactionManager.StartTransaction())
    {
      materialIdByNode = CreateMaterials(db, mtr, bundle, baseLayerName);
      mtr.Commit();
    }
    var (materialIdByGeometry, materialIdByObject) = MapMaterials(bundle, rels, objByGeom, materialIdByNode);

    // 1b - by-object display colours (HAS_COLOR → COLOR nodes). Applied as explicit entity colour on bake; objects
    // with no edge keep AutoCAD's ByLayer default and inherit the restored layer colour (see ResolveLayer).
    var (colorArgbByGeometry, colorArgbByObject) = MapColors(bundle, rels, objByGeom);

    ParseAndBakeAdditionalDefinitions(bundle, baseLayerName);

    // 2 - atomic geometry (objects with a direct DISPLAY/SOLID). Instances + non-geometric elements handled below.
    int count = 0;
    int total = bundle.ObjectAppIds.Count;
    using (session.Phase("Atomic"))
    {
      foreach (var kv in bundle.ObjectAppIds)
      {
        cancellationToken.ThrowIfCancellationRequested();
        onOperationProgressed.Report(new("Converting objects", (double)++count / total));
        int objK = kv.Key;
        string appId = kv.Value;

        bool hasDisplay = rels.DisplayByObject(objK) is { Count: > 0 } || rels.SolidByObject.ContainsKey(objK);
        if (!hasDisplay)
        {
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
          // The layer is created in its own committed transaction so a later per-object abort can't roll it back
          // out from under the layer cache.
          string layerName;
          using (var ltr = doc.TransactionManager.StartTransaction())
          {
            layerName = ResolveLayer(bundle, objK, baseLayerName, db, ltr, layerCache);
            ltr.Commit();
          }
          materialIdByObject.TryGetValue(appId, out ObjectId objMaterial);
          bool hasObjColor = colorArgbByObject.TryGetValue(appId, out int objArgb);

          var ids = new List<ObjectId>();
          using (var tr = doc.TransactionManager.StartTransaction())
          {
            var modelSpace = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
            foreach (var geomK in GeometryIndices(objK, rels))
            {
              materialIdByGeometry.TryGetValue(geomK, out ObjectId geomMaterial);
              ObjectId materialId = objMaterial != ObjectId.Null ? objMaterial : geomMaterial;
              int? argb =
                hasObjColor ? objArgb
                : colorArgbByGeometry.TryGetValue(geomK, out int geomArgb) ? geomArgb
                : null;
              foreach (var entity in DecodeAndAppend(geomK, bundle, modelSpace, tr, ObjectUnits(bundle, objK)))
              {
                entity.Layer = layerName;
                if (materialId != ObjectId.Null)
                {
                  entity.MaterialId = materialId;
                }
                if (argb is int a)
                {
                  entity.Color = ToAcadColor(a);
                }
                ids.Add(entity.ObjectId);
                PostBakeEntity(entity, props, tr);
              }
            }
            tr.Commit();
          }

          if (ids.Count == 0)
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

          bakedObjectIds.UnionWith(ids.Select(i => i.ToString()));
          if (rels.GroupsByObject.ContainsKey(objK))
          {
            bakedIdsByObjK[objK] = ids;
          }
          conversionResults.Add(new(Status.SUCCESS, source, ids[0].ToString(), "Object", null, srcType));
          session.RecordObject(appId, srcType, Status.SUCCESS, null, sw.ElapsedMilliseconds);
        }
        catch (Exception ex) when (!ex.IsFatal())
        {
          session.RecordObject(appId, srcType, Status.ERROR, ex.Message, sw.ElapsedMilliseconds);
          conversionResults.Add(new(Status.ERROR, source, null, null, ex, srcType));
        }
      }
    }

    // 3 - instances: build block definitions from DEFINES → geometry, place one per DISPLAY_INSTANCE edge.
    if (rels.DisplayInstanceEdges.Count > 0)
    {
      onOperationProgressed.Report(new("Converting instances", null));
      using (session.Phase("Instances"))
      using (var itr = doc.TransactionManager.StartTransaction())
      {
        BakeInstances(
          bundle,
          rels,
          db,
          itr,
          baseLayerName,
          layerCache,
          materialIdByGeometry,
          materialIdByObject,
          colorArgbByGeometry,
          colorArgbByObject,
          bakedObjectIds,
          bakedIdsByObjK,
          conversionResults,
          session
        );
        itr.Commit();
      }
    }

    // 4 - authored scene groups (IN_GROUP → CONTAINER(Group) nodes) → native AutoCAD groups.
    if (rels.GroupsByObject.Count > 0)
    {
      using (session.Phase("Groups"))
      using (var gtr = doc.TransactionManager.StartTransaction())
      {
        BakeGroups(bundle, rels, db, gtr, bakedIdsByObjK, baseLayerName, session);
        gtr.Commit();
      }
    }

    return new HostObjectBuilderResult(bakedObjectIds, conversionResults);
  }

  // ── geometry ──────────────────────────────────────────────────────────────────────────────────────────
  // Geometry indices to bake for an object: prefer the lossless SOLID (SAT) blobs, else its DISPLAY meshes.
  private static IEnumerable<int> GeometryIndices(int objK, ArtefactRelations rels)
  {
    if (rels.SolidByObject.TryGetValue(objK, out var solidKs) && solidKs.Count > 0)
    {
      return solidKs;
    }
    if (rels.DisplayByObject(objK) is { } displayEdges)
    {
      return displayEdges.OrderBy(x => x.Ord).Select(e => e.Dst);
    }
    return Array.Empty<int>();
  }

  // Decodes one geometry index and appends the resulting native entity(ies) to the target block-table record,
  // scaled to document units. SAT (unitless) uses the object's units; SGEO carries its own.
  private List<AcadEntity> DecodeAndAppend(
    int geomK,
    ArtefactBundle bundle,
    BlockTableRecord target,
    Transaction tr,
    string fallbackUnits
  )
  {
    var result = new List<AcadEntity>();
    if (!bundle.Geometries.TryGetValue(geomK, out var g))
    {
      return result;
    }

    if (g.Type == RawEncodingFormats.ACAD_SAT)
    {
      foreach (var entity in DecodeSat(g.Content))
      {
        target.AppendEntity(entity);
        tr.AddNewlyCreatedDBObject(entity, true);
        ScaleEntity(entity, fallbackUnits);
        result.Add(entity);
      }
      return result;
    }

    if (g.IsSgeo)
    {
      // Meshes take the fast hand-rolled path (no Base allocation), scaled here.
      if (SgeoDecoder.TryDecodeMesh(g.Content, out var sm))
      {
        var mesh = BuildMesh(sm, target, tr);
        ScaleEntity(mesh, sm.Units);
        result.Add(mesh);
        return result;
      }
      // Curves, points, text and other primitives: decode to a Speckle geometry object and convert via the AutoCAD
      // ToHost converter, which scales from the object's units to doc units itself (so no ScaleEntity here). An
      // unsupported primitive degrades to nothing rather than aborting the whole receive.
      foreach (var entity in ConvertSgeoFallback(g.Content, geomK))
      {
        if (entity is Hatch { Associative: true } hatch)
        {
          // The Region→Hatch converter hatches from temp boundary curves it ERASES afterwards, leaving the hatch
          // associative to erased dependents. Commit then re-evaluates that dangling association (worst inside block
          // definitions after re-parenting) and can hang AutoCAD — the baked hatch is static, so drop associativity.
          hatch.Associative = false;
        }
        if (entity.IsNewObject)
        {
          target.AppendEntity(entity);
          tr.AddNewlyCreatedDBObject(entity, true);
        }
        else if (entity.OwnerId != target.ObjectId)
        {
          // Some converters (Polyline3d, Hatch/Region) self-append to the CURRENT SPACE via the top transaction —
          // re-parent into the target record (matters for block-definition members). See v1's EntityExtensions.AppendToDb.
          target.AssumeOwnershipOf(new ObjectIdCollection { entity.ObjectId });
        }
        result.Add(entity);
      }
    }
    return result;
  }

  // SGEO blob (non-mesh) → Speckle geometry object → native entities via the ToHost converter. Primitives with no
  // AutoCAD converter get a manual fallback (pointclouds → DBPoints, spirals → their display polyline) or are skipped
  // with a warning; a bad blob must not abort the receive.
  private List<AcadEntity> ConvertSgeoFallback(byte[] content, int geomK)
  {
    Base? decoded = null;
    try
    {
      decoded = SgeoDecoder.Decode(content);
      return decoded switch
      {
        Speckle.Objects.Geometry.Pointcloud cloud => PointcloudToPoints(cloud),
        Speckle.Objects.Geometry.Spiral spiral => ConvertViaToHost(spiral.displayValue),
        _ => ConvertViaToHost(decoded),
      };
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      string stage = decoded is null ? "decode" : $"convert of {decoded.speckle_type}";
      _logger.LogWarning(
        ex,
        "Skipped SGEO geometry {GeomK} ({Bytes} bytes) at {Stage}: {Error}",
        geomK,
        content.Length,
        stage,
        ex.Message
      );
      return new List<AcadEntity>();
    }
  }

  // The top-level ToHost converter returns a single Entity for primitives or a list of (Entity, Base) for one-to-many
  // fallback conversions (brep/polycurve/…); both are unwrapped. Returned entities are not yet database-resident.
  private List<AcadEntity> ConvertViaToHost(Base target)
  {
    var converted = _converter.Convert(target);
    return converted switch
    {
      AcadEntity entity => new List<AcadEntity> { entity },
      IEnumerable<(AcadEntity a, Base)> pairs => pairs.Select(p => p.a).ToList(),
      IEnumerable<AcadEntity> many => many.ToList(),
      _ => new List<AcadEntity>(),
    };
  }

  // Pointclouds have no AutoCAD ToHost converter — bake one DBPoint per point (with its colour when present).
  private List<AcadEntity> PointcloudToPoints(Speckle.Objects.Geometry.Pointcloud cloud)
  {
    double f = Units.GetConversionFactor(cloud.units, _converterSettings.Current.SpeckleUnits);
    var pts = cloud.points;
    bool hasColors = cloud.colors.Count * 3 == pts.Count;
    var result = new List<AcadEntity>(pts.Count / 3);
    for (int i = 0; i + 2 < pts.Count; i += 3)
    {
      var point = new DBPoint(new Point3d(pts[i] * f, pts[i + 1] * f, pts[i + 2] * f));
      if (hasColors)
      {
        point.Color = ToAcadColor(cloud.colors[i / 3]);
      }
      result.Add(point);
    }
    return result;
  }

  // ACIS SAT bytes → native entities. Body.AcisIn only reads a file, so round-trip a temp .sat (the established
  // pattern; mirrors RawEncodingToHostConverter.HandleSat but Base-free).
  private static List<AcadEntity> DecodeSat(byte[] satBytes)
  {
    var entities = new List<AcadEntity>();
    string tempFile = Path.GetTempFileName();
    string tempSatFile = Path.ChangeExtension(tempFile, ".sat");
    try
    {
      File.WriteAllBytes(tempSatFile, satBytes);
      using DBObjectCollection imported = Body.AcisIn(tempSatFile);
      foreach (DBObject obj in imported)
      {
        if (obj is AcadEntity entity)
        {
          entities.Add((AcadEntity)entity.Clone());
        }
        obj.Dispose();
      }
    }
    finally
    {
      if (File.Exists(tempSatFile))
      {
        File.Delete(tempSatFile);
      }
      if (File.Exists(tempFile))
      {
        File.Delete(tempFile);
      }
    }
    return entities;
  }

  // SGEO neutral mesh → AutoCAD PolyFaceMesh (must be appended to a BTR before adding vertices/faces; vertex indices
  // are 1-based; faces are Speckle count-prefixed). Mirrors the construction in MeshToHostConverter, Base-free.
  private static PolyFaceMesh BuildMesh(SgeoMesh sm, BlockTableRecord target, Transaction tr)
  {
    var mesh = new PolyFaceMesh();
    mesh.SetDatabaseDefaults();
    target.AppendEntity(mesh);
    tr.AddNewlyCreatedDBObject(mesh, true);

    var v = sm.Vertices;
    int vertexCount = v.Length / 3;
    for (int i = 0; i + 2 < v.Length; i += 3)
    {
      var vertex = new PolyFaceMeshVertex(new Point3d(v[i], v[i + 1], v[i + 2]));
      int idx = i / 3;
      if (idx < sm.Colors.Length)
      {
        try
        {
          var c = System.Drawing.Color.FromArgb(sm.Colors[idx]);
          vertex.Color = AcadColor.FromRgb(c.R, c.G, c.B);
        }
        catch (Exception e) when (!e.IsFatal())
        {
          // a bad vertex color must not abort the mesh
        }
      }
      mesh.AppendVertex(vertex);
      tr.AddNewlyCreatedDBObject(vertex, true);
    }

    var f = sm.Faces;
    int p = 0;
    while (p < f.Length)
    {
      int n = f[p];
      if (n < 3)
      {
        n += 3; // legacy 0 -> triangle, 1 -> quad
      }
      if (n == 3 && p + 3 < f.Length)
      {
        AppendFace(mesh, tr, f[p + 1], f[p + 2], f[p + 3], null, vertexCount);
      }
      else if (n == 4 && p + 4 < f.Length)
      {
        AppendFace(mesh, tr, f[p + 1], f[p + 2], f[p + 3], f[p + 4], vertexCount);
      }
      else if (n > 4 && p + n < f.Length)
      {
        for (int k = 1; k < n - 1; k++) // fan-triangulate n-gons (PolyFaceMesh faces are tri/quad only)
        {
          AppendFace(mesh, tr, f[p + 1], f[p + 1 + k], f[p + 2 + k], null, vertexCount);
        }
      }
      else
      {
        break;
      }
      p += n + 1;
    }

    return mesh;
  }

  private static void AppendFace(PolyFaceMesh mesh, Transaction tr, int i1, int i2, int i3, int? i4, int vertexCount)
  {
    if (i1 < 0 || i2 < 0 || i3 < 0 || i1 >= vertexCount || i2 >= vertexCount || i3 >= vertexCount)
    {
      return;
    }
    // PolyFaceMesh face vertex indices are 1-based; 0 in the 4th slot marks a triangle.
    var face =
      i4 is int q && q >= 0 && q < vertexCount
        ? new FaceRecord((short)(i1 + 1), (short)(i2 + 1), (short)(i3 + 1), (short)(q + 1))
        : new FaceRecord((short)(i1 + 1), (short)(i2 + 1), (short)(i3 + 1), 0);
    mesh.AppendFaceRecord(face);
    tr.AddNewlyCreatedDBObject(face, true);
  }

  private void ScaleEntity(AcadEntity entity, string? units)
  {
    if (units is not { Length: > 0 } u)
    {
      return;
    }
    double factor = Units.GetConversionFactor(u, _converterSettings.Current.SpeckleUnits);
    if (Math.Abs(factor - 1.0) > 1e-12)
    {
      entity.TransformBy(Matrix3d.Scaling(factor, Point3d.Origin));
    }
  }

  // ── instances ─────────────────────────────────────────────────────────────────────────────────────────
#pragma warning disable CA1506
  private void BakeInstances(
#pragma warning restore CA1506
    ArtefactBundle bundle,
    ArtefactRelations rels,
    Database db,
    Transaction tr,
    string baseLayerName,
    HashSet<string> layerCache,
    Dictionary<int, ObjectId> materialIdByGeometry,
    Dictionary<string, ObjectId> materialIdByObject,
    Dictionary<int, int> colorArgbByGeometry,
    Dictionary<string, int> colorArgbByObject,
    HashSet<string> bakedObjectIds,
    Dictionary<int, List<ObjectId>> bakedIdsByObjK,
    HashSet<ReceiveConversionResult> conversionResults,
    ArtefactSessionLog session
  )
  {
    var blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);

    var defIdByNode = BuildDefinitions(
      bundle,
      rels,
      blockTable,
      tr,
      baseLayerName,
      materialIdByGeometry,
      colorArgbByGeometry,
      session
    );

    // placements: one BlockReference per DISPLAY_INSTANCE edge (object → INSTANCE node); an object may place several.
    var modelSpace = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
    PlaceInstances(
      bundle,
      rels,
      db,
      tr,
      baseLayerName,
      layerCache,
      materialIdByObject,
      colorArgbByObject,
      defIdByNode,
      modelSpace,
      bakedObjectIds,
      bakedIdsByObjK,
      conversionResults,
      session
    );
  }

  // Builds every DEFINITION node into a BlockTableRecord, returning node K → block ObjectId. A DEFINITION owns its
  // geometry directly (DEFINES → geometry, grouped by member ordinal with the authoritative SAT solid preferred over
  // its display-mesh shadow) and may contain nested block placements (DEFINES_INSTANCE → INSTANCE node), built
  // depth-first (memoized, cycle-guarded) as nested BlockReferences — mirrors RhinoHostObjectArtefactBuilder.
  private Dictionary<int, ObjectId> BuildDefinitions(
    ArtefactBundle bundle,
    ArtefactRelations rels,
    BlockTable blockTable,
    Transaction tr,
    string baseLayerName,
    Dictionary<int, ObjectId> materialIdByGeometry,
    Dictionary<int, int> colorArgbByGeometry,
    ArtefactSessionLog session
  )
  {
    var docUnits = _converterSettings.Current.SpeckleUnits;
    var defIdByNode = new Dictionary<int, ObjectId>();
    var defBuilding = new HashSet<int>();

    ObjectId BuildDefinition(int defNodeK)
    {
      if (defIdByNode.TryGetValue(defNodeK, out var already))
      {
        return already; // already built (a shared nested definition is reached from several parents)
      }
      if (!bundle.Nodes.TryGetValue(defNodeK, out var defNode) || defNode.Kind != NodeKind.Definition)
      {
        return ObjectId.Null;
      }
      if (!defBuilding.Add(defNodeK))
      {
        return ObjectId.Null; // cycle guard — never stack-overflow on a bad bundle
      }
      session.Increment("definitionsSeen");

      var btr = new BlockTableRecord
      {
        Name = UniqueBlockName(
          blockTable,
          _autocadContext.RemoveInvalidChars($"{defNode.Name ?? "Definition"}-(def-{defNodeK})-{baseLayerName}")
        ),
        Origin = Point3d.Origin,
      };
      ObjectId defId = blockTable.Add(btr);
      tr.AddNewlyCreatedDBObject(btr, true);

      int memberCount = 0;

      // direct geometry members. Definition geometry is in the model's source units (no per-object context here) —
      // pass the bundle units so a SAT member from a metre model isn't baked 1000x off in a millimetre doc.
      if (rels.DefinesByDefinition.TryGetValue(defNodeK, out var geomKs))
      {
        rels.DefinesOrdByDefinition.TryGetValue(defNodeK, out var ords);
        foreach (var memberGeomKs in GroupDefinesByMember(geomKs, ords, bundle))
        {
          foreach (var geomK in memberGeomKs)
          {
            materialIdByGeometry.TryGetValue(geomK, out ObjectId geomMaterial);
            bool hasGeomColor = colorArgbByGeometry.TryGetValue(geomK, out int geomArgb);
            foreach (var entity in DecodeAndAppend(geomK, bundle, btr, tr, bundle.Units))
            {
              if (geomMaterial != ObjectId.Null)
              {
                entity.MaterialId = geomMaterial;
              }
              if (hasGeomColor)
              {
                entity.Color = ToAcadColor(geomArgb);
              }
              memberCount++;
            }
          }
        }
      }

      // nested block members (DEFINES_INSTANCE → INSTANCE node): build the child definition first, then place it
      // inside this BTR as a nested BlockReference with the nested placement's own transform.
      if (rels.DefinesInstanceByDefinition.TryGetValue(defNodeK, out var nestedInstNodeKs))
      {
        foreach (var instNodeK in nestedInstNodeKs)
        {
          if (!bundle.Nodes.TryGetValue(instNodeK, out var nestedInst) || nestedInst.DefRef is not int childDefNodeK)
          {
            continue;
          }
          ObjectId childDefId = BuildDefinition(childDefNodeK);
          if (childDefId == ObjectId.Null)
          {
            session.Increment("nestedInstancesUnresolved");
            continue;
          }
          Matrix3d nestedMatrix = BuildMatrix3d(
            nestedInst.Transform,
            nestedInst.Units is { Length: > 0 } u ? u : docUnits,
            docUnits
          );
          var nestedRef = new BlockReference(Point3d.Origin.TransformBy(nestedMatrix), childDefId)
          {
            BlockTransform = nestedMatrix,
          };
          btr.AppendEntity(nestedRef);
          tr.AddNewlyCreatedDBObject(nestedRef, true);
          memberCount++;
          session.Increment("nestedInstancesPlaced");
        }
      }

      defBuilding.Remove(defNodeK);

      if (memberCount == 0)
      {
        session.Increment("definitionsEmpty");
        btr.Erase();
        return ObjectId.Null;
      }
      defIdByNode[defNodeK] = defId;
      return defId;
    }

    foreach (var kv in bundle.Nodes)
    {
      if (kv.Value.Kind == NodeKind.Definition)
      {
        BuildDefinition(kv.Key);
      }
    }

    return defIdByNode;
  }

  private void PlaceInstances(
    ArtefactBundle bundle,
    ArtefactRelations rels,
    Database db,
    Transaction tr,
    string baseLayerName,
    HashSet<string> layerCache,
    Dictionary<string, ObjectId> materialIdByObject,
    Dictionary<string, int> colorArgbByObject,
    Dictionary<int, ObjectId> defIdByNode,
    BlockTableRecord modelSpace,
    HashSet<string> bakedObjectIds,
    Dictionary<int, List<ObjectId>> bakedIdsByObjK,
    HashSet<ReceiveConversionResult> conversionResults,
    ArtefactSessionLog session
  )
  {
    var docUnits = _converterSettings.Current.SpeckleUnits;
    foreach (var edge in rels.DisplayInstanceEdges)
    {
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
      if (instNode.DefRef is not int defNodeK || !defIdByNode.TryGetValue(defNodeK, out ObjectId defId))
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
        Matrix3d matrix = BuildMatrix3d(
          instNode.Transform,
          instNode.Units is { Length: > 0 } u ? u : docUnits,
          docUnits
        );
        Point3d insertion = Point3d.Origin.TransformBy(matrix);
        string layerName = ResolveLayer(bundle, objK, baseLayerName, db, tr, layerCache);
        var blockRef = new BlockReference(insertion, defId) { BlockTransform = matrix, Layer = layerName };
        if (materialIdByObject.TryGetValue(appId, out ObjectId objMaterial) && objMaterial != ObjectId.Null)
        {
          blockRef.MaterialId = objMaterial;
        }
        if (colorArgbByObject.TryGetValue(appId, out int objArgb))
        {
          blockRef.Color = ToAcadColor(objArgb); // ByBlock members pick this up from the reference
        }
        modelSpace.AppendEntity(blockRef);
        tr.AddNewlyCreatedDBObject(blockRef, true);
        bakedObjectIds.Add(blockRef.ObjectId.ToString());
        if (rels.GroupsByObject.ContainsKey(objK))
        {
          if (!bakedIdsByObjK.TryGetValue(objK, out var grouped))
          {
            bakedIdsByObjK[objK] = grouped = new List<ObjectId>();
          }
          grouped.Add(blockRef.ObjectId); // an object may place several instances; all of them join its group(s)
        }
        conversionResults.Add(
          new(Status.SUCCESS, source, blockRef.ObjectId.ToString(), "Instance (Block)", null, srcType)
        );
        session.RecordObject(appId, srcType, Status.SUCCESS, null, sw.ElapsedMilliseconds);
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        session.RecordObject(appId, srcType, Status.ERROR, ex.Message, sw.ElapsedMilliseconds);
        conversionResults.Add(new(Status.ERROR, source, null, null, ex, srcType));
      }
    }
  }

  // ── groups ────────────────────────────────────────────────────────────────────────────────────────────
  // Authored scene groups: inverts GroupsByObject (object → its CONTAINER(Group) nodes) to group → member entity ids
  // and creates one native Group per node in the document group dictionary (mirrors the v1 AutocadGroupBaker). The
  // name carries the baseLayerName suffix so PreClean purges this model's groups on re-receive; the dictionary
  // requires unique keys (unlike Rhino's group table), so a clashing name gets a -N suffix. A member that failed to
  // bake (or is non-geometric) is skipped; the group keeps its other members. Smaller groups first, as in v1.
  private void BakeGroups(
    ArtefactBundle bundle,
    ArtefactRelations rels,
    Database db,
    Transaction tr,
    Dictionary<int, List<ObjectId>> bakedIdsByObjK,
    string baseLayerName,
    ArtefactSessionLog session
  )
  {
    var membersByGroup = new Dictionary<int, List<ObjectId>>();
    foreach (var kv in rels.GroupsByObject)
    {
      if (!bakedIdsByObjK.TryGetValue(kv.Key, out var ids))
      {
        continue;
      }
      foreach (int groupK in kv.Value)
      {
        if (!membersByGroup.TryGetValue(groupK, out var list))
        {
          membersByGroup[groupK] = list = new List<ObjectId>();
        }
        list.AddRange(ids);
      }
    }
    if (membersByGroup.Count == 0)
    {
      return;
    }

    var groupDictionary = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForWrite);
    foreach (var kv in membersByGroup.OrderBy(g => g.Value.Count))
    {
      try
      {
        bundle.Nodes.TryGetValue(kv.Key, out var node);
        var rawName = node?.Name is { Length: > 0 } n ? n : "Group";
        var baseName = $"{_autocadContext.RemoveInvalidChars(rawName)} ({baseLayerName})";
        string name = baseName;
        for (int i = 1; groupDictionary.Contains(name); i++)
        {
          name = $"{baseName}-{i}";
        }

        var memberIds = new ObjectIdCollection();
        foreach (var id in kv.Value)
        {
          memberIds.Add(id);
        }
        var group = new Group(name, true); // NOTE: this constructor sets both description and name
        group.Append(memberIds);
        groupDictionary.SetAt(name, group);
        tr.AddNewlyCreatedDBObject(group, true);
        session.Increment("groupsBaked");
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogError(ex, "Failed to bake AutoCAD group node {GroupK}", kv.Key);
      }
    }
  }

  // Groups a definition's DEFINES geometry Ks by member ordinal (index-aligned with ords), then within each member
  // prefers the authoritative SAT solid over its display mesh(es); a member with no solid yields all its geometry.
  // Member order is preserved. When ords are absent (older bundle) each geometry is its own member — i.e. no grouping.
  private static IEnumerable<List<int>> GroupDefinesByMember(List<int> geomKs, List<int>? ords, ArtefactBundle bundle)
  {
    var members = new List<List<int>>();
    var indexByOrd = new Dictionary<int, int>();
    for (int i = 0; i < geomKs.Count; i++)
    {
      int ord = ords is not null && i < ords.Count ? ords[i] : -(i + 1); // absent ords → unique key per geometry
      if (!indexByOrd.TryGetValue(ord, out int idx))
      {
        idx = members.Count;
        indexByOrd[ord] = idx;
        members.Add(new List<int>());
      }
      members[idx].Add(geomKs[i]);
    }
    foreach (var geoms in members)
    {
      var solids = geoms
        .Where(k => bundle.Geometries.TryGetValue(k, out var g) && g.Type == RawEncodingFormats.ACAD_SAT)
        .ToList();
      yield return solids.Count > 0 ? solids : geoms;
    }
  }

  private static string UniqueBlockName(BlockTable blockTable, string baseName)
  {
    string name = baseName;
    int i = 1;
    while (blockTable.Has(name))
    {
      name = $"{baseName}-{i++}";
    }
    return name;
  }

  // ── layers ────────────────────────────────────────────────────────────────────────────────────────────
  // AutoCAD has a flat layer namespace: the scene-view segments are joined into one hyphen-separated layer name under
  // the model prefix (mirrors AutocadLayerBaker's flattening), then created on demand.
  private string ResolveLayer(
    ArtefactBundle bundle,
    int objK,
    string baseLayerName,
    Database db,
    Transaction tr,
    HashSet<string> cache
  )
  {
    var segments = SceneViewResolver.SegmentsWithColor(bundle, objK); // (name, argb) so layers get their source colour
    string name =
      segments.Count == 0 ? baseLayerName : $"{baseLayerName}-{string.Join("-", segments.Select(s => s.Name))}";
    name = _autocadContext.RemoveInvalidChars(name);

    // The flattened AutoCAD layer stands in for the whole segment chain — colour it from the innermost
    // (leaf-most) segment that carries one, matching what the object would inherit in the source app.
    int? argb = null;
    for (int i = segments.Count - 1; i >= 0; i--)
    {
      if (segments[i].Argb is int a)
      {
        argb = a;
        break;
      }
    }
    return GetOrCreateLayer(name, db, tr, cache, argb);
  }

  private static string GetOrCreateLayer(
    string layerName,
    Database db,
    Transaction tr,
    HashSet<string> cache,
    int? argb
  )
  {
    if (cache.Contains(layerName))
    {
      return layerName;
    }
    var layerTable = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
    if (!layerTable.Has(layerName))
    {
      layerTable.UpgradeOpen();
      var record = new LayerTableRecord { Name = layerName };
      if (argb is int a)
      {
        record.Color = ToAcadColor(a);
      }
      layerTable.Add(record);
      tr.AddNewlyCreatedDBObject(record, true);
    }
    cache.Add(layerName);
    return layerName;
  }

  // ── materials ─────────────────────────────────────────────────────────────────────────────────────────
  private Dictionary<int, ObjectId> CreateMaterials(
    Database db,
    Transaction tr,
    ArtefactBundle bundle,
    string baseLayerName
  )
  {
    var result = new Dictionary<int, ObjectId>();
    var materialDict = (DBDictionary)tr.GetObject(db.MaterialDictionaryId, OpenMode.ForWrite);
    foreach (var kv in bundle.Nodes)
    {
      var n = kv.Value;
      if (n.Kind != NodeKind.Material)
      {
        continue;
      }
      try
      {
        string matName = _autocadContext.RemoveInvalidChars($"{n.Name ?? "Material"}-(mat-{kv.Key})-{baseLayerName}");
        var systemDiffuse = System.Drawing.Color.FromArgb(n.Argb ?? unchecked((int)0xFFFFFFFF));
        MaterialMap map = new();
        MaterialOpacityComponent opacity = new(n.Opacity ?? 1.0, map);
        EntityColor entityDiffuse = new(systemDiffuse.R, systemDiffuse.G, systemDiffuse.B);
        MaterialColor diffuseColor = new(Method.Override, 1, entityDiffuse);
        MaterialDiffuseComponent diffuse = new(diffuseColor, map);
        var mat = new AcadMaterial
        {
          Name = matName,
          Opacity = opacity,
          Diffuse = diffuse,
        };
        ObjectId id = materialDict.SetAt(matName, mat);
        tr.AddNewlyCreatedDBObject(mat, true);
        result[kv.Key] = id;
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogError(ex, "Failed to create artefact material for node {Node}", kv.Key);
      }
    }
    return result;
  }

  private static (Dictionary<int, ObjectId> byGeometry, Dictionary<string, ObjectId> byObject) MapMaterials(
    ArtefactBundle bundle,
    ArtefactRelations rels,
    Dictionary<int, int> objByGeom,
    Dictionary<int, ObjectId> materialIdByNode
  )
  {
    var byGeometry = new Dictionary<int, ObjectId>();
    var byObject = new Dictionary<string, ObjectId>(StringComparer.Ordinal);
    foreach (var kv in rels.MaterialByGeometry)
    {
      if (!materialIdByNode.TryGetValue(kv.Value, out ObjectId id))
      {
        continue;
      }
      byGeometry[kv.Key] = id;
      if (objByGeom.TryGetValue(kv.Key, out int objK) && bundle.ObjectAppIds.TryGetValue(objK, out var appId))
      {
        byObject[appId] = id;
      }
    }
    return (byGeometry, byObject);
  }

  // ── colours ───────────────────────────────────────────────────────────────────────────────────────────
  // By-object display colours: HAS_COLOR (geometry → COLOR node) mapped both by geometry (definition members) and by
  // owning object appId (atomic objects + block references), mirroring MapMaterials.
  private static (Dictionary<int, int> byGeometry, Dictionary<string, int> byObject) MapColors(
    ArtefactBundle bundle,
    ArtefactRelations rels,
    Dictionary<int, int> objByGeom
  )
  {
    var byGeometry = new Dictionary<int, int>();
    var byObject = new Dictionary<string, int>(StringComparer.Ordinal);
    foreach (var kv in rels.ColorByGeometry)
    {
      if (!bundle.Nodes.TryGetValue(kv.Value, out var n) || n.Kind != NodeKind.Color || n.Argb is not int argb)
      {
        continue;
      }
      byGeometry[kv.Key] = argb;
      if (objByGeom.TryGetValue(kv.Key, out int objK) && bundle.ObjectAppIds.TryGetValue(objK, out var appId))
      {
        byObject[appId] = argb;
      }
    }
    return (byGeometry, byObject);
  }

  // AutoCAD colours have no alpha channel (transparency is a separate entity property) — strip it.
  private static AcadColor ToAcadColor(int argb)
  {
    var c = System.Drawing.Color.FromArgb(argb);
    return AcadColor.FromRgb(c.R, c.G, c.B);
  }

  // ── helpers ───────────────────────────────────────────────────────────────────────────────────────────
  private Matrix3d BuildMatrix3d(string? csv, string units, string docUnits)
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

    double scale = Units.GetConversionFactor(units, docUnits);
    d[3] *= scale; // translation x
    d[7] *= scale; // translation y
    d[11] *= scale; // translation z

    var m3d = new Matrix3d(d);
    if (!m3d.IsScaledOrtho())
    {
      m3d = new Matrix3d(MakePerpendicular(m3d));
    }
    return m3d;
  }

  // Re-orthogonalise a sheared/non-ortho basis so AutoCAD accepts it as a BlockTransform (cross-product fix-up).
  // https://forums.autodesk.com/t5/net/set-blocktransform-values/m-p/6452121
  private static double[] MakePerpendicular(Matrix3d matrix)
  {
    Vector3d right = new(matrix[0, 0], matrix[1, 0], matrix[2, 0]);
    Vector3d up = new(matrix[0, 1], matrix[1, 1], matrix[2, 1]);
    Vector3d newForward = right.CrossProduct(up).GetNormal();
    Vector3d newUp = newForward.CrossProduct(right).GetNormal();
    return
    [
      right.X,
      newUp.X,
      newForward.X,
      matrix[0, 3],
      right.Y,
      newUp.Y,
      newForward.Y,
      matrix[1, 3],
      right.Z,
      newUp.Z,
      newForward.Z,
      matrix[2, 3],
      0,
      0,
      0,
      1,
    ];
  }

  private static string ObjectUnits(ArtefactBundle bundle, int objK) =>
    bundle.Properties.TryGetValue(objK, out var props)
    && props.TryGetValue("units", out var v)
    && v is string s
    && s.Length > 0
      ? s
      : bundle.Units;

  // Deletes the previous receive of this model: model-space entities on its layers + its block definitions. Empty
  // layers are left for reuse. Best-effort — a clean failure must not block the receive.
  private void PreClean(Database db, string baseLayerName)
  {
    try
    {
      using var tr = db.TransactionManager.StartTransaction();
      var modelSpace = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
      foreach (ObjectId id in modelSpace)
      {
        if (
          tr.GetObject(id, OpenMode.ForRead) is AcadEntity entity
          && entity.Layer.StartsWith(baseLayerName, StringComparison.Ordinal)
        )
        {
          entity.UpgradeOpen();
          entity.Erase();
        }
      }

      // purge this model's groups from the prior receive — they carry the baseLayerName suffix (BakeGroups).
      // Collect first, then erase: erasing while enumerating the dictionary invalidates the enumerator.
      var groupDictionary = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead);
      var staleGroupIds = new List<ObjectId>();
      foreach (DBDictionaryEntry entry in groupDictionary)
      {
        if (entry.Key.Contains(baseLayerName))
        {
          staleGroupIds.Add(entry.Value);
        }
      }
      foreach (var groupId in staleGroupIds)
      {
        if (tr.GetObject(groupId, OpenMode.ForWrite) is Group staleGroup)
        {
          staleGroup.Erase();
        }
      }

      var blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
      foreach (ObjectId btrId in blockTable)
      {
        var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
        if (!btr.IsLayout && !btr.IsAnonymous && btr.Name.Contains(baseLayerName))
        {
          try
          {
            btr.UpgradeOpen();
            btr.Erase();
          }
          catch (Exception ex) when (!ex.IsFatal())
          {
            _logger.LogWarning(ex, "Could not erase prior block definition {Name}", btr.Name);
          }
        }
      }

      tr.Commit();
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, "Artefact receive pre-clean failed for '{BaseLayer}'", baseLayerName);
    }
  }

  protected virtual void PreCleanAdditional(string baseLayerName) { }

  protected virtual void ParseAndBakeAdditionalDefinitions(ArtefactBundle bundle, string baseLayerName) { }

  protected virtual void PostBakeEntity(AcadEntity entity, Dictionary<string, object?>? properties, Transaction tr) { }

  private static string SrcType(Dictionary<string, object?>? props) =>
    props is not null && props.TryGetValue("speckle_type", out var v) && v is string s && s.Length > 0
      ? s
      : "Speckle Object";

  /// <summary>Minimal <see cref="Base"/> carrier used only as the <c>source</c> of a conversion report entry. A plain
  /// <see cref="Base"/> (not a custom subclass) so the assembly-scanned TypeLoader accepts it.</summary>
  private static Base Source(string appId) => new() { applicationId = appId, id = appId };
}
