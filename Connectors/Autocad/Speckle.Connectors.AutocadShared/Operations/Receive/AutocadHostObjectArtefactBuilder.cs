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
/// (<see cref="SgeoDecoder.TryDecodeMesh"/>) built straight into a <see cref="PolyFaceMesh"/>; layers are the flat
/// AutoCAD layer namespace projected from the scene view; materials from MATERIAL nodes (HAS_MATERIAL). Instances
/// follow the host-agnostic model: a DEFINITION node owns its geometry directly (DEFINES → geometry) baked into a
/// <see cref="BlockTableRecord"/>, and each DISPLAY_INSTANCE edge places it as a <see cref="BlockReference"/>. The
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
  private readonly IThreadContext _threadContext;
  private readonly AutocadContext _autocadContext;
  private readonly ISdkActivityFactory _activityFactory;
  private readonly ILogger<AutocadHostObjectArtefactBuilder> _logger;

  public AutocadHostObjectArtefactBuilder(
    IConverterSettingsStore<AutocadConversionSettings> converterSettings,
    IThreadContext threadContext,
    AutocadContext autocadContext,
    ISdkActivityFactory activityFactory,
    ILogger<AutocadHostObjectArtefactBuilder> logger
  )
  {
    _converterSettings = converterSettings;
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

    using var docLock = doc.LockDocument();

    // 0 - clean previous receive of this model (entities on this model's layers + its block definitions).
    PreClean(db, baseLayerName);

    using var tr = db.TransactionManager.StartTransaction();
    try
    {
      // 1 - materials (MATERIAL nodes) into the document material dictionary; map geometry/object → material id.
      Dictionary<int, ObjectId> materialIdByNode;
      using (session.Phase("Materials"))
      {
        materialIdByNode = CreateMaterials(db, tr, bundle, baseLayerName);
      }
      var (materialIdByGeometry, materialIdByObject) = MapMaterials(bundle, rels, objByGeom, materialIdByNode);

      // 2 - atomic geometry (objects with a direct DISPLAY/SOLID). Instances + non-geometric elements handled below.
      var modelSpace = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
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
            string layerName = ResolveLayer(bundle, objK, baseLayerName, db, tr, layerCache);
            materialIdByObject.TryGetValue(appId, out ObjectId objMaterial);

            var ids = new List<ObjectId>();
            foreach (var geomK in GeometryIndices(objK, rels))
            {
              materialIdByGeometry.TryGetValue(geomK, out ObjectId geomMaterial);
              ObjectId materialId = objMaterial != ObjectId.Null ? objMaterial : geomMaterial;
              foreach (var entity in DecodeAndAppend(geomK, bundle, modelSpace, tr, ObjectUnits(bundle, objK)))
              {
                entity.Layer = layerName;
                if (materialId != ObjectId.Null)
                {
                  entity.MaterialId = materialId;
                }
                ids.Add(entity.ObjectId);
              }
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
        {
          BakeInstances(
            bundle,
            rels,
            db,
            tr,
            baseLayerName,
            layerCache,
            materialIdByGeometry,
            materialIdByObject,
            bakedObjectIds,
            conversionResults,
            session
          );
        }
      }

      tr.Commit();
    }
    catch
    {
      tr.Abort();
      throw;
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

    if (g.IsSgeo && SgeoDecoder.TryDecodeMesh(g.Content, out var sm))
    {
      var mesh = BuildMesh(sm, target, tr);
      ScaleEntity(mesh, sm.Units);
      result.Add(mesh);
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
    HashSet<string> bakedObjectIds,
    HashSet<ReceiveConversionResult> conversionResults,
    ArtefactSessionLog session
  )
  {
    var docUnits = _converterSettings.Current.SpeckleUnits;
    var blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);

    // definitions: a DEFINITION node owns its geometry directly (DEFINES → geometry blobs) → a BlockTableRecord.
    var defIdByNode = new Dictionary<int, ObjectId>();
    foreach (var kv in bundle.Nodes)
    {
      if (kv.Value.Kind != NodeKind.Definition || !rels.DefinesByDefinition.TryGetValue(kv.Key, out var geomKs))
      {
        continue;
      }
      session.Increment("definitionsSeen");
      var btr = new BlockTableRecord
      {
        Name = UniqueBlockName(
          blockTable,
          _autocadContext.RemoveInvalidChars($"{kv.Value.Name ?? "Definition"}-(def-{kv.Key})-{baseLayerName}")
        ),
        Origin = Point3d.Origin,
      };
      ObjectId defId = blockTable.Add(btr);
      tr.AddNewlyCreatedDBObject(btr, true);

      int memberCount = 0;
      foreach (var geomK in geomKs)
      {
        materialIdByGeometry.TryGetValue(geomK, out ObjectId geomMaterial);
        foreach (var entity in DecodeAndAppend(geomK, bundle, btr, tr, docUnits))
        {
          if (geomMaterial != ObjectId.Null)
          {
            entity.MaterialId = geomMaterial;
          }
          memberCount++;
        }
      }

      if (memberCount == 0)
      {
        session.Increment("definitionsEmpty");
        btr.Erase();
        continue;
      }
      defIdByNode[kv.Key] = defId;
    }

    // placements: one BlockReference per DISPLAY_INSTANCE edge (object → INSTANCE node); an object may place several.
    var modelSpace = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
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
        modelSpace.AppendEntity(blockRef);
        tr.AddNewlyCreatedDBObject(blockRef, true);
        bakedObjectIds.Add(blockRef.ObjectId.ToString());
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
    var segments = SceneViewResolver.Segments(bundle, objK);
    string name = segments.Count == 0 ? baseLayerName : $"{baseLayerName}-{string.Join("-", segments)}";
    name = _autocadContext.RemoveInvalidChars(name);
    return GetOrCreateLayer(name, db, tr, cache);
  }

  private static string GetOrCreateLayer(string layerName, Database db, Transaction tr, HashSet<string> cache)
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

  private static string SrcType(Dictionary<string, object?>? props) =>
    props is not null && props.TryGetValue("speckle_type", out var v) && v is string s && s.Length > 0
      ? s
      : "Speckle Object";

  /// <summary>Minimal <see cref="Base"/> carrier used only as the <c>source</c> of a conversion report entry. A plain
  /// <see cref="Base"/> (not a custom subclass) so the assembly-scanned TypeLoader accepts it.</summary>
  private static Base Source(string appId) => new() { applicationId = appId, id = appId };
}
