using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.LayerManager;
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
/// (<see cref="SgeoDecoder.TryDecodeMesh(ReadOnlySpan{byte}, out SgeoMesh)"/>) built straight into a <see cref="PolyFaceMesh"/> (or a
/// <see cref="SubDMesh"/> past its 16-bit vertex-index ceiling); every other SGEO
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
  private readonly AutocadLayerBaker _layerBaker;
  private readonly AutocadInstanceBaker _instanceBaker;
  private readonly IAutocadMaterialBaker _materialBaker;

  // Why the last geometry blob produced no entities (missing blob / decode / convert), so an object that bakes nothing
  // reports the actual cause instead of the opaque "did not convert to any native geometry" [ENG-8819]. Set by
  // DecodeAndAppend, consumed + reset per object by the caller — the bake is single-threaded on the main thread.
  private string? _lastDecodeFailure;

  /// <summary>Every received layer starts with this — the receive stamp the pre-clean, the material purge and the
  /// Layer Properties Manager root filter all key on. ONE definition: the root filter expression was once a
  /// separate hardcode of a previous stamp and silently matched nothing after the stamp changed [ENG-9331].</summary>
  private const string LAYER_STAMP_PREFIX = "SPK-";
  private Matrix3d? _receivePlacementTransform;

  public AutocadHostObjectArtefactBuilder(
    IConverterSettingsStore<AutocadConversionSettings> converterSettings,
    IRootToHostConverter converter,
    IThreadContext threadContext,
    AutocadContext autocadContext,
    ISdkActivityFactory activityFactory,
    ILogger<AutocadHostObjectArtefactBuilder> logger,
    AutocadLayerBaker layerBaker,
    AutocadInstanceBaker instanceBaker,
    IAutocadMaterialBaker materialBaker
  )
  {
    _converterSettings = converterSettings;
    _converter = converter;
    _threadContext = threadContext;
    _autocadContext = autocadContext;
    _activityFactory = activityFactory;
    _logger = logger;
    _layerBaker = layerBaker;
    _instanceBaker = instanceBaker;
    _materialBaker = materialBaker;
  }

  public Task<HostObjectBuilderResult> Build(
    ArtefactBundle bundle,
    ArtefactReceiveTarget target,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  ) =>
    _threadContext.RunOnMain(() =>
      BakeAll(bundle, target.ProjectName, target.ModelName, onOperationProgressed, cancellationToken)
    );

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
    // The SAME card stamp the legacy connector uses [ENG-9377]: both paths' pre-receive cleanups match on it
    // (Contains), so a legacy bake is replaced by an artefact receive of the same card and vice versa, instead of
    // the two conventions stacking a second copy of the model next to each other's orphans.
    var baseLayerName = _autocadContext.RemoveInvalidChars($"{LAYER_STAMP_PREFIX}{projectName}-{modelName}-");
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
    _receivePlacementTransform = ReadModelPlacementTransform(bundle, docUnits);
    var rels = bundle.Relations;
    var objByGeom = rels.ObjectByGeometry();
    // Baked entities are identified by their AutoCAD HANDLE (decimal, as GetSpeckleApplicationId spells it) — the id
    // space the receiver model card, the conversion report and DocumentExtensions.GetObjects all speak. Reporting
    // ObjectId.ToString() (a parenthesised in-memory pointer) instead meant "Highlight" on a received card resolved
    // nothing and errored with "No objects found to highlight" [ENG-8833].
    var bakedObjectIds = new HashSet<string>();
    var conversionResults = new HashSet<ReceiveConversionResult>();
    var layerCache = new HashSet<string>(StringComparer.Ordinal);
    // objK → its baked entity ids, tracked only for grouped objects (IN_GROUP) so step 4 can rebuild native groups.
    var bakedIdsByObjK = new Dictionary<int, List<ObjectId>>();

    using var docLock = doc.LockDocument();

    // 0 - clean previous receive of this model (entities on this model's layers + its block definitions).
    PreClean(db, baseLayerName);
    PreCleanAdditional(baseLayerName);
    EnsureLayerFilter(db, projectName, modelName, baseLayerName);

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

    // Layer materials (NODE_HAS_MATERIAL → container node): projected onto the created materials so ResolveLayer can
    // write each rebuilt layer record's MaterialId — the authored assignment, not just the flattened render carrier
    // [ENG-9346].
    var layerMaterialByNode = new Dictionary<int, ObjectId>();
    foreach (var kv in rels.MaterialByNode)
    {
      if (materialIdByNode.TryGetValue(kv.Value, out ObjectId layerMatId))
      {
        layerMaterialByNode[kv.Key] = layerMatId;
      }
    }

    // 1b - by-object display colours (HAS_COLOR → COLOR nodes). Applied as explicit entity colour on bake; objects
    // with no edge keep AutoCAD's ByLayer default and inherit the restored layer colour (see ResolveLayer).
    var (colorArgbByGeometry, colorArgbByObject) = MapColors(bundle, rels, objByGeom);

    // The rel-built member index (DEFINES_MEMBER/PLACES): geometry K / INSTANCE node K → member object K, restoring
    // each block-definition member's identity — its layer, colour source and properties [ENG-9344]. Empty on a
    // pre-vocab bundle (AutoCAD never wrote the Rhino-style @speckle.* stamps, so there is no stamp fallback).
    var memberIndex = BuildMemberIndexFromRels(rels);

    ParseAndBakeAdditionalDefinitions(bundle, baseLayerName, session);

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

        var props = bundle.ObjectProperties(objK);
        var source = Source(appId);
        var srcType = SrcType(props);
        var sw = Stopwatch.StartNew();
        _lastDecodeFailure = null;
        try
        {
          // The layer is created in its own committed transaction so a later per-object abort can't roll it back
          // out from under the layer cache.
          string layerName;
          using (var ltr = doc.TransactionManager.StartTransaction())
          {
            layerName = ResolveLayer(bundle, objK, baseLayerName, db, ltr, layerCache, layerMaterialByNode);
            ltr.Commit();
          }
          materialIdByObject.TryGetValue(appId, out ObjectId objMaterial);
          bool hasObjColor = colorArgbByObject.TryGetValue(appId, out int objArgb);
          // ACI index / explicit ByBlock, when the sender recorded them — they outrank the flattened ARGB [ENG-9117].
          AcadColor? nativeColor = NativeColorFromProperties(props);

          // (ObjectId, handle) per baked entity: the ObjectId drives native grouping below, while the model card and
          // the conversion report identify entities by their HANDLE — see the note on bakedObjectIds [ENG-8833].
          var baked = new List<(ObjectId Id, string Handle)>();
          using (var tr = doc.TransactionManager.StartTransaction())
          {
            var modelSpace = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
            var (primaryKs, fallbackKs) = GeometryIndices(objK, rels, bundle);
            BakeGeometry(primaryKs);
            if (baked.Count == 0)
            {
              // the solid blob(s) produced nothing (SAT AcisIn failure) — bake the DISPLAY shadow instead [ENG-8820]
              BakeGeometry(fallbackKs);
            }
            tr.Commit();

            void BakeGeometry(IReadOnlyList<int> geomKs)
            {
              foreach (var geomK in geomKs)
              {
                materialIdByGeometry.TryGetValue(geomK, out ObjectId geomMaterial);
                ObjectId materialId = objMaterial != ObjectId.Null ? objMaterial : geomMaterial;
                int? argb =
                  hasObjColor ? objArgb
                  : colorArgbByGeometry.TryGetValue(geomK, out int geomArgb) ? geomArgb
                  : null;
                foreach (var entity in DecodeAndAppend(geomK, bundle, modelSpace, tr, ObjectUnits(bundle, objK)))
                {
                  if (_receivePlacementTransform is Matrix3d placement)
                  {
                    entity.TransformBy(placement);
                  }
                  entity.Layer = layerName;
                  if (materialId != ObjectId.Null)
                  {
                    entity.MaterialId = materialId;
                  }
                  if (nativeColor is AcadColor native)
                  {
                    entity.Color = native;
                  }
                  else if (argb is int a)
                  {
                    entity.Color = ToAcadColor(a);
                  }
                  // The handle is read while the entity is still open in this transaction (it is assigned on append).
                  baked.Add((entity.ObjectId, entity.GetSpeckleApplicationId()));
                  PostBakeEntity(entity, props, tr, session); // Civil3D hook (property sets) — fires for fallback bakes too
                }
              }
            }
          }

          if (baked.Count == 0)
          {
            // Carry the decode/convert failure through to the report card — a dropped curve/point must say why
            // [ENG-8819], not just that nothing landed.
            var reason = _lastDecodeFailure ?? "did not convert to any native geometry";
            session.RecordObject(appId, srcType, Status.ERROR, reason, sw.ElapsedMilliseconds);
            conversionResults.Add(new(Status.ERROR, source, null, null, new ConversionException(reason), srcType));
            continue;
          }

          bakedObjectIds.UnionWith(baked.Select(b => b.Handle));
          if (rels.GroupsByObject.ContainsKey(objK))
          {
            bakedIdsByObjK[objK] = baked.Select(b => b.Id).ToList();
          }
          conversionResults.Add(new(Status.SUCCESS, source, baked[0].Handle, "Object", null, srcType));
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
          layerMaterialByNode,
          memberIndex,
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
  // The SOLID preference is a preference, NOT a commitment [ENG-8820]: a foreign solid blob (a Rhino 3dm this
  // host can never read) is filtered out up front, and the caller retries the Fallback list when the Primary
  // produced no entities (a SAT from a newer ACIS that Body.AcisIn rejects). The display meshes exist precisely
  // so a host that can't read the raw format still gets renderable geometry.
  private static (IReadOnlyList<int> Primary, IReadOnlyList<int> Fallback) GeometryIndices(
    int objK,
    ArtefactRelations rels,
    ArtefactBundle bundle
  )
  {
    var display = rels.DisplayByObject(objK) is { } displayEdges
      ? displayEdges.OrderBy(x => x.Ord).Select(e => e.Dst).ToList()
      : new List<int>();
    if (rels.SolidByObject.TryGetValue(objK, out var solidKs) && solidKs.Count > 0)
    {
      var decodable = solidKs
        .Where(k => bundle.Geometries.TryGetValue(k, out var g) && g.Type == RawEncodingFormats.ACAD_SAT)
        .ToList();
      if (decodable.Count > 0)
      {
        return (decodable, display);
      }
    }
    return (display, Array.Empty<int>());
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
      _lastDecodeFailure = $"geom {geomK}: no blob for this geometry index in the bundle";
      return result;
    }

    if (g.Type == RawEncodingFormats.ACAD_SAT)
    {
      // A throwing AcisIn (SAT from a newer ACIS version) must degrade to "no entities", not error the whole
      // object — the caller falls back to the DISPLAY meshes when the solid produced nothing [ENG-8820].
      List<AcadEntity> satEntities;
      try
      {
        satEntities = DecodeSat(g.Content);
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        _lastDecodeFailure = $"geom {geomK} (SAT) decode failed — {ex.GetType().Name}: {ex.Message}";
        _logger.LogWarning(ex, "SAT decode failed for geometry {GeomK} ({Bytes} bytes)", geomK, g.Content.Length);
        return result;
      }
      foreach (var entity in satEntities)
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
      var converted = decoded switch
      {
        Speckle.Objects.Geometry.Pointcloud cloud => PointcloudToPoints(cloud),
        Speckle.Objects.Geometry.Spiral spiral => ConvertViaToHost(spiral.displayValue),
        _ => ConvertViaToHost(decoded),
      };
      if (converted.Count == 0)
      {
        // decode + convert both ran without throwing but produced nothing (e.g. a converter returned an unhandled
        // result shape) — record it so it isn't a silent drop.
        _lastDecodeFailure = $"geom {geomK} ({decoded.speckle_type}): converter returned no native geometry";
        _logger.LogWarning("Skipped SGEO geometry {GeomK}: {Reason}", geomK, _lastDecodeFailure);
      }
      return converted;
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      string stage = decoded is null ? "decode" : $"convert of {decoded.speckle_type}";
      _lastDecodeFailure = $"geom {geomK} (SGEO) {stage} failed — {ex.GetType().Name}: {ex.Message}";
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

  // SGEO neutral mesh → native AutoCAD mesh. Mirrors the construction in MeshToHostConverter, Base-free: a
  // PolyFaceMesh (must be appended to a BTR before adding vertices/faces; vertex indices are 1-based; faces are
  // Speckle count-prefixed), or a SubDMesh once the mesh outgrows PolyFaceMesh's 16-bit vertex indices [ENG-8836].
  private static AcadEntity BuildMesh(SgeoMesh sm, BlockTableRecord target, Transaction tr)
  {
    if (sm.Vertices.Length / 3 > MAX_POLYFACE_MESH_VERTICES)
    {
      return BuildSubDMesh(sm, target, tr);
    }

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

  /// <summary>
  /// A <see cref="PolyFaceMesh"/> addresses its vertices through <see cref="FaceRecord"/>, whose 1-based vertex
  /// indices are 16-bit — so it cannot hold more than <see cref="short.MaxValue"/> vertices. Beyond that the index
  /// casts in <see cref="AppendFace"/> silently wrapped negative and the mesh baked as garbage [ENG-8836]; larger
  /// meshes become a native MESH (<see cref="SubDMesh"/>) instead, whose face array is 32-bit.
  /// </summary>
  private const int MAX_POLYFACE_MESH_VERTICES = short.MaxValue;

  // A mesh too large for a PolyFaceMesh [ENG-8836]. The MESH entity takes its whole topology in one call: a vertex
  // array plus a count-prefixed, 0-based face array — the layout SGEO already carries — with 32-bit indices, and
  // per-vertex colours on its EntityColor array. Smooth level 0 keeps the faceting exactly as sent.
  private static SubDMesh BuildSubDMesh(SgeoMesh sm, BlockTableRecord target, Transaction tr)
  {
    var v = sm.Vertices;
    int vertexCount = v.Length / 3;
    var points = new Point3d[vertexCount];
    for (int i = 0; i < vertexCount; i++)
    {
      points[i] = new Point3d(v[i * 3], v[i * 3 + 1], v[i * 3 + 2]);
    }

    using var vertices = new Point3dCollection(points);
    var faceArray = new Int32Collection(SubDMeshFaces(sm.Faces, vertexCount));

    var mesh = new SubDMesh();
    mesh.SetDatabaseDefaults();
    mesh.SetSubDMesh(vertices, faceArray, 0);

    if (sm.Colors.Length == vertexCount)
    {
      try
      {
        var colors = new EntityColor[vertexCount];
        for (int i = 0; i < vertexCount; i++)
        {
          var c = System.Drawing.Color.FromArgb(sm.Colors[i]);
          colors[i] = new EntityColor(c.R, c.G, c.B);
        }
        mesh.VertexColorArray = colors;
      }
      catch (Exception e) when (!e.IsFatal())
      {
        // a bad vertex color must not abort the mesh (same rule as the polyface path)
      }
    }

    target.AppendEntity(mesh);
    tr.AddNewlyCreatedDBObject(mesh, true);
    return mesh;
  }

  // SGEO faces are already the count-prefixed, 0-based layout SetSubDMesh expects, so this only normalizes it: the
  // legacy 0/1 count encoding (triangle/quad), repeated corners, and faces that are truncated or point outside the
  // vertex array. SetSubDMesh takes the topology in one call and rejects the WHOLE mesh on a single bad face, so a
  // malformed face is dropped here instead of costing us the entire mesh. Mirrors MeshToHostConverter.
  private static int[] SubDMeshFaces(int[] faces, int vertexCount)
  {
    var result = new List<int>(faces.Length);
    var corners = new List<int>(4);
    int p = 0;
    while (p < faces.Length)
    {
      int n = faces[p];
      if (n < 3)
      {
        n += 3; // legacy 0 -> triangle, 1 -> quad
      }
      if (p + n >= faces.Length)
      {
        break; // truncated face list
      }

      corners.Clear();
      int lastCorner = -1;
      for (int k = p + 1; k <= p + n; k++)
      {
        int index = faces[k];
        if (index < 0 || index >= vertexCount)
        {
          corners.Clear(); // face points outside the vertex array — drop it whole
          break;
        }
        // Collapse a repeated corner: a quad written [a, b, c, c] is really a triangle, and a face left with fewer
        // than 3 distinct corners has no area to bake.
        if (index != lastCorner)
        {
          corners.Add(index);
          lastCorner = index;
        }
      }

      if (corners.Count > 3 && corners[0] == lastCorner)
      {
        corners.RemoveAt(corners.Count - 1); // closed face: last corner repeats the first
      }

      if (corners.Count >= 3)
      {
        result.Add(corners.Count);
        result.AddRange(corners);
      }

      p += n + 1;
    }

    return result.ToArray();
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
    Dictionary<int, ObjectId> layerMaterialByNode,
    DefinitionMemberIndex memberIndex,
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
      db,
      blockTable,
      tr,
      baseLayerName,
      layerCache,
      layerMaterialByNode,
      memberIndex,
      materialIdByGeometry,
      materialIdByObject,
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
      layerMaterialByNode,
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
  // Each member's identity rides the memberIndex join (geometry/INSTANCE K → member object K): its own LAYER (the
  // same ResolveLayer every top-level object uses, shared layerCache) and its native colour source (ACI / explicit
  // ByBlock from its eav row) [ENG-9344]. An unindexed member (pre-vocab bundle) bakes exactly as before.
  private Dictionary<int, ObjectId> BuildDefinitions(
    ArtefactBundle bundle,
    ArtefactRelations rels,
    Database db,
    BlockTable blockTable,
    Transaction tr,
    string baseLayerName,
    HashSet<string> layerCache,
    Dictionary<int, ObjectId> layerMaterialByNode,
    DefinitionMemberIndex memberIndex,
    Dictionary<int, ObjectId> materialIdByGeometry,
    Dictionary<string, ObjectId> materialIdByObject,
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
        foreach (var (preferredKs, fallbackKs) in GroupDefinesByMember(geomKs, ords, bundle))
        {
          int before = memberCount;
          BakeMember(preferredKs);
          if (memberCount == before)
          {
            // the member's solid blob produced nothing — bake its DISPLAY shadow instead [ENG-8820]
            BakeMember(fallbackKs);
          }
        }

        void BakeMember(List<int> memberGeomKs)
        {
          foreach (var geomK in memberGeomKs)
          {
            materialIdByGeometry.TryGetValue(geomK, out ObjectId geomMaterial);
            bool hasGeomColor = colorArgbByGeometry.TryGetValue(geomK, out int geomArgb);
            // The member's own identity, joined through DEFINES_MEMBER (geometry K → member object K): its LAYER and
            // its native colour semantics (ACI index / explicit ByBlock) restore from its object row exactly as a
            // top-level object's would [ENG-9344]. Unindexed (pre-vocab bundle) → the pre-join behaviour below.
            var (memberLayer, memberNativeColor) = ResolveMemberIdentity(
              bundle,
              geomK,
              memberIndex,
              baseLayerName,
              db,
              tr,
              layerCache,
              layerMaterialByNode
            );
            foreach (var entity in DecodeAndAppend(geomK, bundle, btr, tr, bundle.Units))
            {
              if (memberLayer is not null)
              {
                entity.Layer = memberLayer;
              }
              if (geomMaterial != ObjectId.Null)
              {
                entity.MaterialId = geomMaterial;
              }
              entity.Color = ResolveMemberColor(memberNativeColor, hasGeomColor, geomArgb, memberLayer);
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
          // A nested-block member owns no geometry K, so its whole identity join — layer, colour source, material —
          // hangs off its INSTANCE node K instead (PLACES, inverted into ObjectByInstance) [ENG-9344].
          ApplyNestedMemberIdentity(
            nestedRef,
            bundle,
            rels,
            instNodeK,
            memberIndex,
            materialIdByObject,
            baseLayerName,
            db,
            tr,
            layerCache,
            layerMaterialByNode
          );
          btr.AppendEntity(nestedRef);
          tr.AddNewlyCreatedDBObject(nestedRef, true);
          memberCount++;
          session.Increment("nestedInstancesPlaced");
        }
      }

      defBuilding.Remove(defNodeK);

      if (memberCount == 0)
      {
        // A definition whose members all failed to decode leaves every placement of it unbaked — say why [ENG-8819].
        _logger.LogWarning(
          "Block definition {DefNodeK} ('{Name}') baked no members: {Reason}",
          defNodeK,
          defNode.Name,
          _lastDecodeFailure ?? "no member geometry decoded"
        );
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
    Dictionary<int, ObjectId> layerMaterialByNode,
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
      var props = bundle.ObjectProperties(objK);
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
        if (_receivePlacementTransform is Matrix3d placement)
        {
          matrix = matrix.PreMultiplyBy(placement);
        }
        Point3d insertion = Point3d.Origin.TransformBy(matrix);
        string layerName = ResolveLayer(bundle, objK, baseLayerName, db, tr, layerCache, layerMaterialByNode);
        var blockRef = new BlockReference(insertion, defId) { BlockTransform = matrix, Layer = layerName };
        if (materialIdByObject.TryGetValue(appId, out ObjectId objMaterial) && objMaterial != ObjectId.Null)
        {
          blockRef.MaterialId = objMaterial;
        }
        if (NativeColorFromProperties(props) is AcadColor nativeColor)
        {
          blockRef.Color = nativeColor; // ACI / explicit ByBlock recorded by the sender [ENG-9117]
        }
        else if (colorArgbByObject.TryGetValue(appId, out int objArgb))
        {
          blockRef.Color = ToAcadColor(objArgb); // ByBlock members pick this up from the reference
        }
        modelSpace.AppendEntity(blockRef);
        tr.AddNewlyCreatedDBObject(blockRef, true);
        string blockRefHandle = blockRef.GetSpeckleApplicationId(); // handle, not ObjectId — see bakedObjectIds
        bakedObjectIds.Add(blockRefHandle);
        if (rels.GroupsByObject.ContainsKey(objK))
        {
          if (!bakedIdsByObjK.TryGetValue(objK, out var grouped))
          {
            bakedIdsByObjK[objK] = grouped = new List<ObjectId>();
          }
          grouped.Add(blockRef.ObjectId); // an object may place several instances; all of them join its group(s)
        }
        conversionResults.Add(new(Status.SUCCESS, source, blockRefHandle, "Instance (Block)", null, srcType));
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
  // PREFERS the authoritative SAT solid over its display mesh(es) — but doesn't commit [ENG-8820]: the caller
  // bakes Preferred and retries Fallback (the member's remaining display geometry) when it produced no entities
  // (foreign/undecodable solid blob). A member with no decodable solid yields all its geometry up front. Member
  // order is preserved. When ords are absent (older bundle) each geometry is its own member — i.e. no grouping.
  private static IEnumerable<(List<int> Preferred, List<int> Fallback)> GroupDefinesByMember(
    List<int> geomKs,
    List<int>? ords,
    ArtefactBundle bundle
  )
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
      if (solids.Count > 0)
      {
        var solidSet = new HashSet<int>(solids);
        yield return (solids, geoms.Where(k => !solidSet.Contains(k)).ToList());
      }
      else
      {
        yield return (geoms, new List<int>());
      }
    }
  }

  // The rel-built member index [ENG-9344]: DEFINES_MEMBER (25, def → member object, ord = member ordinal) joined
  // against the definition's DEFINES ords recovers geometry K → member object K; PLACES (24, member object →
  // INSTANCE node) inverts to INSTANCE K → member object K. Both maps are empty on a pre-vocab bundle (no rel 25
  // rows), and every consumer of the index degrades to the pre-join behaviour. Mirrors
  // RhinoHostObjectArtefactBuilder.TryBuildMemberIndexFromRels, minus the @speckle.* stamp fallback AutoCAD never wrote.
  private static DefinitionMemberIndex BuildMemberIndexFromRels(ArtefactRelations rels)
  {
    var byGeometry = new Dictionary<int, int>();
    var byInstance = new Dictionary<int, int>();
    foreach (var kv in rels.MemberObjectsByDefinition)
    {
      // member ordinal → member object K for this definition
      var objByOrd = new Dictionary<int, int>();
      var memberOrds = rels.MemberOrdByDefinition[kv.Key];
      for (int m = 0; m < kv.Value.Count; m++)
      {
        objByOrd[memberOrds[m]] = kv.Value[m];
      }
      if (
        !rels.DefinesByDefinition.TryGetValue(kv.Key, out var geomKs)
        || !rels.DefinesOrdByDefinition.TryGetValue(kv.Key, out var geomOrds)
      )
      {
        continue;
      }
      for (int i = 0; i < geomKs.Count; i++)
      {
        if (objByOrd.TryGetValue(geomOrds[i], out int memberObjK))
        {
          byGeometry[geomKs[i]] = memberObjK;
        }
      }
    }
    foreach (var kv in rels.PlacesByObject)
    {
      byInstance[kv.Value] = kv.Key;
    }
    return new DefinitionMemberIndex(byGeometry, byInstance);
  }

  /// <summary>Geometry K / INSTANCE node K → the definition member OBJECT K that owns it (DEFINES_MEMBER + PLACES),
  /// restoring member identity — layer, colour source, properties — inside rebuilt block definitions [ENG-9344].</summary>
  private sealed record DefinitionMemberIndex(
    Dictionary<int, int> ObjectByGeometry,
    Dictionary<int, int> ObjectByInstance
  );

  // A definition member's colour: native semantics (ACI / explicit ByBlock from its eav row) outrank the flattened
  // ARGB [ENG-9117]; an explicit colour edge is kept as an explicit override. A member with NEITHER is ByLayer —
  // the source default; it sits on its own restored layer (memberLayer), so ByLayer inherits the right colour
  // natively [ENG-9344]. Only a pre-vocab bundle (no member identity, and ByLayer members carried the retired
  // ENG-8825 pinned edge) keeps the ByBlock fallback, so an edge-less member still inherits its placing
  // BlockReference's colour there [ENG-8822].
  private static AcadColor ResolveMemberColor(
    AcadColor? nativeColor,
    bool hasGeomColor,
    int geomArgb,
    string? memberLayer
  ) =>
    nativeColor
    ?? (
      hasGeomColor ? ToAcadColor(geomArgb)
      : memberLayer is not null ? AcadColor.FromColorIndex(ColorMethod.ByLayer, 256)
      : AcadColor.FromColorIndex(ColorMethod.ByBlock, 0)
    );

  // A direct member's identity join (geometry K → member object K): the member's own LAYER — resolved through the
  // SAME ResolveLayer every top-level object uses, shared layerCache — and its native colour semantics (ACI index /
  // explicit ByBlock) from its eav row [ENG-9344]. Unindexed (pre-vocab bundle) → (null, null), the pre-join bake.
  private (string? Layer, AcadColor? NativeColor) ResolveMemberIdentity(
    ArtefactBundle bundle,
    int geomK,
    DefinitionMemberIndex memberIndex,
    string baseLayerName,
    Database db,
    Transaction tr,
    HashSet<string> layerCache,
    Dictionary<int, ObjectId> layerMaterialByNode
  )
  {
    if (!memberIndex.ObjectByGeometry.TryGetValue(geomK, out int memberObjK))
    {
      return (null, null);
    }
    string layer = ResolveMemberLayer(bundle, memberObjK, baseLayerName, db, tr, layerCache, layerMaterialByNode);
    var memberProps = bundle.ObjectProperties(memberObjK);
    return (layer, NativeColorFromProperties(memberProps));
  }

  // AutoCAD's layer-0 convention: a definition member authored on layer "0" is a chameleon — at draw time it takes
  // the PLACING REFERENCE's layer, so ByLayer resolves through the reference, not through layer 0 itself. That rule
  // fires only on the layer named literally "0", so such a member must bake there — putting it on the prefixed card
  // layer ("SPK-…-0") froze it to source layer 0's own (usually white) colour and the member stopped following its
  // reference [ENG-9344]. Layer "0" always exists and can't be deleted, so no creation is needed.
  private string ResolveMemberLayer(
    ArtefactBundle bundle,
    int memberObjK,
    string baseLayerName,
    Database db,
    Transaction tr,
    HashSet<string> layerCache,
    Dictionary<int, ObjectId> layerMaterialByNode
  )
  {
    var segments = SceneViewResolver.Segments(bundle, memberObjK);
    return segments.Count == 1 && segments[0] == "0"
      ? "0"
      : ResolveLayer(bundle, memberObjK, baseLayerName, db, tr, layerCache, layerMaterialByNode);
  }

  // A nested-block member's identity join: it owns no geometry K, so layer, material and colour source all hang off
  // its INSTANCE node K (PLACES, inverted into ObjectByInstance) [ENG-9344]. Its material is object-plane
  // (OBJECT_HAS_MATERIAL → materialIdByObject via its appId) and its colour is object-sourced (OBJECT_HAS_COLOR /
  // the legacy tagged HAS_COLOR), mirroring what PlaceInstances applies to a top-level placement.
  private void ApplyNestedMemberIdentity(
    BlockReference nestedRef,
    ArtefactBundle bundle,
    ArtefactRelations rels,
    int instNodeK,
    DefinitionMemberIndex memberIndex,
    Dictionary<string, ObjectId> materialIdByObject,
    string baseLayerName,
    Database db,
    Transaction tr,
    HashSet<string> layerCache,
    Dictionary<int, ObjectId> layerMaterialByNode
  )
  {
    if (!memberIndex.ObjectByInstance.TryGetValue(instNodeK, out int memberObjK))
    {
      return;
    }
    nestedRef.Layer = ResolveMemberLayer(bundle, memberObjK, baseLayerName, db, tr, layerCache, layerMaterialByNode);
    if (
      bundle.ObjectAppIds.TryGetValue(memberObjK, out var appId)
      && materialIdByObject.TryGetValue(appId, out ObjectId material)
      && material != ObjectId.Null
    )
    {
      nestedRef.MaterialId = material;
    }
    var props = bundle.ObjectProperties(memberObjK);
    if (NativeColorFromProperties(props) is AcadColor native)
    {
      nestedRef.Color = native; // ACI / explicit ByBlock recorded by the sender [ENG-9117]
    }
    else if (
      rels.ColorByObject.TryGetValue(memberObjK, out int colorK)
      && bundle.Nodes.TryGetValue(colorK, out var colorNode)
      && colorNode.Kind == NodeKind.Color
      && colorNode.Argb is int argb
    )
    {
      nestedRef.Color = ToAcadColor(argb); // the placement's own colour (OBJECT_HAS_COLOR)
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
    HashSet<string> cache,
    Dictionary<int, ObjectId> layerMaterialByNode
  )
  {
    // (name, argb, nodeK) — colour + the segment's node K, so the flattened layer also recovers the source layer's
    // authored render material (NODE_HAS_MATERIAL) [ENG-9346].
    var segments = SceneViewResolver.SegmentsWithAppearance(bundle, objK);
    // baseLayerName is the legacy card prefix and already ends with a dash [ENG-9377].
    string name =
      segments.Count == 0 ? baseLayerName : $"{baseLayerName}{string.Join("-", segments.Select(s => s.Name))}";
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
    // Same innermost-wins rule for the layer's authored material.
    ObjectId materialId = ObjectId.Null;
    for (int i = segments.Count - 1; i >= 0; i--)
    {
      if (segments[i].NodeK is int nodeK && layerMaterialByNode.TryGetValue(nodeK, out ObjectId m))
      {
        materialId = m;
        break;
      }
    }
    return GetOrCreateLayer(name, db, tr, cache, argb, materialId);
  }

  private static string GetOrCreateLayer(
    string layerName,
    Database db,
    Transaction tr,
    HashSet<string> cache,
    int? argb,
    ObjectId materialId
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
      if (materialId != ObjectId.Null)
      {
        // The source layer's authored render material — restored on the record itself so ByLayer objects keep
        // inheriting it after the round trip, not just drawing with a flattened copy [ENG-9346].
        record.MaterialId = materialId;
      }
      layerTable.Add(record);
      tr.AddNewlyCreatedDBObject(record, true);
    }
    else if (argb is int a)
    {
      // PreClean keeps emptied layers for reuse, so a re-receive must push the sender's CURRENT colour onto the
      // existing record — most baked entities are ByLayer, so this is the colour the user sees. Sender wins over a
      // receiver-side manual recolour, as the v1 delete-and-recreate did [ENG-9330]. No sender colour → untouched.
      var record = (LayerTableRecord)tr.GetObject(layerTable[layerName], OpenMode.ForRead);
      AcadColor color = ToAcadColor(a);
      if (!record.Color.Equals(color))
      {
        record.UpgradeOpen();
        record.Color = color;
      }
    }
    cache.Add(layerName);
    return layerName;
  }

  // ── layer filter ──────────────────────────────────────────────────────────────────────────────────────
  // Layer Properties Manager filter tree: "Speckle" → "{project}-{model}", the nested one selecting exactly this
  // model's layers. Ports the v1 AutocadLayerBaker.CreateLayerFilter onto the artefact stamp ("SPK-{project}-{model}-…")
  // and is idempotent — a re-receive refreshes the nested filter's expression instead of adding a duplicate
  // [ENG-9331]. Best-effort: a filter failure must not block the receive. (AutoCAD only repaints the tree after the
  // palette is closed and reopened.)
  private void EnsureLayerFilter(Database db, string projectName, string modelName, string baseLayerName)
  {
    const string ROOT_FILTER_NAME = "Speckle";
    try
    {
      LayerFilterTree tree = db.LayerFilters;
      LayerFilterCollection rootFilters = tree.Root.NestedFilters;
      LayerFilter? group = null;
      foreach (LayerFilter existing in rootFilters)
      {
        if (existing.Name == ROOT_FILTER_NAME)
        {
          group = existing;
          break;
        }
      }
      // AutoCAD intersects a nested filter with its parent, so the root MUST match the stamp or every nested
      // per-model filter shows "0 of N layers". Derived from the stamp and re-applied on every receive, so a
      // drawing that still carries a stale root expression heals itself [ENG-9331].
      string rootExpression = $"NAME==\"{EscapeWildcards(LAYER_STAMP_PREFIX)}*\"";
      if (group is null)
      {
        group = new LayerFilter { Name = ROOT_FILTER_NAME, FilterExpression = rootExpression };
        rootFilters.Add(group);
      }
      else if (group.FilterExpression != rootExpression)
      {
        group.FilterExpression = rootExpression;
      }

      string filterName = _autocadContext.RemoveInvalidChars($"{projectName}-{modelName}");
      string expression = $"NAME==\"{EscapeWildcards(baseLayerName)}*\"";
      LayerFilter? modelFilter = null;
      foreach (LayerFilter nested in group.NestedFilters)
      {
        if (nested.Name == filterName)
        {
          modelFilter = nested;
          break;
        }
      }
      if (modelFilter is null)
      {
        group.NestedFilters.Add(new LayerFilter { Name = filterName, FilterExpression = expression });
      }
      else
      {
        modelFilter.FilterExpression = expression;
      }
      db.LayerFilters = tree;
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogWarning(ex, "Could not create the Speckle layer filter for '{BaseLayer}'", baseLayerName);
    }
  }

  // AutoCAD wildcard specials inside a NAME== pattern must be escaped with a reverse quote, or a model named
  // "v1.2" / "a,b" matches the wrong layers.
  private static readonly char[] s_wildcardSpecials = "`#@.*?~[],".ToCharArray();

  private static string EscapeWildcards(string s)
  {
    var sb = new System.Text.StringBuilder(s.Length);
    foreach (char c in s)
    {
      if (Array.IndexOf(s_wildcardSpecials, c) >= 0)
      {
        sb.Append('`');
      }
      sb.Append(c);
    }
    return sb.ToString();
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

    // Object-plane edges (OBJECT_HAS_MATERIAL, rel 26): a material assigned to a block REFERENCE, which owns no
    // geometry and so never appears in objByGeom — resolve it straight through the object dictionary, mirroring
    // MapColors' ColorByObject pass [ENG-9119].
    foreach (var kv in rels.MaterialByObject)
    {
      if (
        materialIdByNode.TryGetValue(kv.Value, out ObjectId id)
        && bundle.ObjectAppIds.TryGetValue(kv.Key, out var appId)
      )
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

    // Object-sourced edges (ord=1): a block placement's own colour. It owns no geometry, so it never appears in
    // objByGeom — resolve it straight through the object dictionary [ENG-8822].
    foreach (var kv in rels.ColorByObject)
    {
      if (
        bundle.Nodes.TryGetValue(kv.Value, out var n)
        && n.Kind == NodeKind.Color
        && n.Argb is int argb
        && bundle.ObjectAppIds.TryGetValue(kv.Key, out var appId)
      )
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

  /// <summary>The AutoCAD colour METHOD, when the sender recorded it as object properties — the ARGB-only COLOR
  /// node cannot express either. An ACI colour comes back as <see cref="ColorMethod.ByAci"/> with its original
  /// index (so CTB plot styles, standards checks and index-reading scripts keep working), and an explicitly
  /// ByBlock entity comes back inheriting rather than pinned to a fixed RGB [ENG-9117]. A bundle from any other
  /// host (or an older AutoCAD send) carries neither key, and the caller falls back to the ARGB edge.</summary>
  private static AcadColor? NativeColorFromProperties(PropertyView props)
  {
    if (props.GetString(AutocadColorSemanticKeys.SOURCE) is not { } src)
    {
      return null;
    }
    if (src == "block")
    {
      return AcadColor.FromColorIndex(ColorMethod.ByBlock, 0);
    }
    // ACI is a 0..256 index (0 = ByBlock, 256 = ByLayer); anything else is not an index we can restore.
    if (
      src == "aci"
      && props.TryGetValue(AutocadColorSemanticKeys.INDEX, out var raw)
      && TryReadInt(raw, out int aci)
      && aci is >= 0 and <= 256
    )
    {
      return AcadColor.FromColorIndex(ColorMethod.ByAci, (short)aci);
    }
    return null;
  }

  // eav round-trips a numeric scalar as whichever width the parquet column landed on, so accept the lot.
  private static bool TryReadInt(object? value, out int result)
  {
    switch (value)
    {
      case int i:
        result = i;
        return true;
      case long l:
        result = (int)l;
        return true;
      case short s:
        result = s;
        return true;
      case double d:
        result = (int)d;
        return true;
      case string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed):
        result = parsed;
        return true;
      default:
        result = 0;
        return false;
    }
  }

  // ── helpers ───────────────────────────────────────────────────────────────────────────────────────────
  private Matrix3d? ReadModelPlacementTransform(ArtefactBundle bundle, string docUnits)
  {
    if (
      !_converterSettings.Current.ApplyTransform
      || !bundle.ModelProperties.TryGetValue("modelPlacement", out object? placementObject)
      || placementObject is not Dictionary<string, object?> placement
      || !placement.TryGetValue("appliedToGeometry", out object? appliedObject)
      || appliedObject is not bool appliedToGeometry
      || appliedToGeometry
      || !placement.TryGetValue("transform", out object? transformObject)
      || transformObject is not string transform
    )
    {
      return null;
    }

    string sourceUnits =
      placement.TryGetValue("units", out object? unitsObject) && unitsObject is string units ? units : bundle.Units;
    return BuildMatrix3d(transform, sourceUnits, docUnits);
  }

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
    bundle.ObjectProperties(objK).GetString("units") is { Length: > 0 } s ? s : bundle.Units;

  // Deletes the previous receive of this model — through the SAME per-card janitors the legacy builder's
  // PreReceiveDeepClean uses (they match on Contains, and both paths now stamp with the same SPK- prefix), so a
  // legacy bake is replaced by an artefact receive of the same card and vice versa [ENG-9377]: entities AND their
  // layer records (so re-created layers pick up the sender's current colour + material), block references and
  // definitions, render materials [ENG-9329]. Groups are artefact-only (the legacy path has no group cleanup), so
  // they keep a hand-rolled purge by name stamp. Best-effort — a clean failure must not block the receive.
  private void PreClean(Database db, string baseLayerName)
  {
    try
    {
      _layerBaker.DeleteAllLayersByPrefix(baseLayerName);
      _instanceBaker.PurgeInstances(baseLayerName);
      _materialBaker.PurgeMaterials(baseLayerName);

      // purge this model's groups from the prior receive — they carry the baseLayerName suffix (BakeGroups).
      // Collect first, then erase: erasing while enumerating the dictionary invalidates the enumerator.
      using var tr = db.TransactionManager.StartTransaction();
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
      tr.Commit();
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, "Artefact receive pre-clean failed for '{BaseLayer}'", baseLayerName);
    }
  }

  protected virtual void PreCleanAdditional(string baseLayerName) { }

  protected virtual void ParseAndBakeAdditionalDefinitions(
    ArtefactBundle bundle,
    string baseLayerName,
    ArtefactSessionLog session
  ) { }

  protected virtual void PostBakeEntity(
    AcadEntity entity,
    PropertyView properties,
    Transaction tr,
    ArtefactSessionLog session
  ) { }

  private static string SrcType(PropertyView props) =>
    props.GetString("speckle_type") is { Length: > 0 } s ? s : "Speckle Object";

  /// <summary>Minimal <see cref="Base"/> carrier used only as the <c>source</c> of a conversion report entry. A plain
  /// <see cref="Base"/> (not a custom subclass) so the assembly-scanned TypeLoader accepts it.</summary>
  private static Base Source(string appId) => new() { applicationId = appId, id = appId };
}
