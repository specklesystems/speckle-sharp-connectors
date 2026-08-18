using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Rhino;
using Rhino.DocObjects;
using Rhino.Render;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Diagnostics;
using Speckle.Connectors.Common.Instances;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.Rhino.Extensions;
using Speckle.Connectors.Rhino.HostApp;
using Speckle.Converters.Common;
using Speckle.Converters.Rhino;
using Speckle.Converters.Rhino.ToHost.Helpers;
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
using RG = Rhino.Geometry;
using RhinoRenderMaterial = Rhino.Render.RenderMaterial;
using SOG = Speckle.Objects.Geometry;

namespace Speckle.Connectors.Rhino.Operations.Receive;

/// <summary>
/// Bakes a Speckle 4.0 artefact <see cref="ArtefactBundle"/> <b>directly</b> into the Rhino document, talking only to
/// the neutral dense-int graph + raw Rhino API — no v1 <c>Base</c>/<c>DataObject</c>/<c>Collection</c>/proxy types and
/// no traversal/converter pipeline. Solids come from raw 3dm blobs (<see cref="RawEncodingToHost.Convert3dm"/>), meshes
/// from SGEO (<see cref="SgeoDecoder.TryDecodeMesh(ReadOnlySpan{byte}, out SgeoMesh)"/>) built straight into a <see cref="RG.Mesh"/>, and other SGEO
/// primitives (curves, points) decode via <see cref="SgeoDecoder.Decode"/> + the Rhino ToHost converter; layers from the
/// COLLECTION tree; materials from MATERIAL nodes (HAS_MATERIAL). Instances follow the host-agnostic model used by
/// Revit and Rhino alike: a DEFINITION node owns its geometry directly (DEFINES → geometry), and each DISPLAY_INSTANCE
/// edge (object → INSTANCE node) places that definition with the instance node's transform. The receive-side twin of
/// the send-side <c>RhinoArtifactRootObjectBuilder</c>.
/// </summary>
[SuppressMessage(
  "Maintainability",
  "CA1506:Avoid excessive class coupling",
  Justification = "Top-level artefact receive orchestrator; coupling to converters, host API, bundle graph and value nodes is inherent."
)]
public class RhinoHostObjectArtefactBuilder : IArtifactHostObjectBuilder
{
  private readonly IConverterSettingsStore<RhinoConversionSettings> _converterSettings;
  private readonly IRootToHostConverter _converter;
  private readonly IThreadContext _threadContext;
  private readonly ISdkActivityFactory _activityFactory;
  private readonly ILogger<RhinoHostObjectArtefactBuilder> _logger;

  // Last per-geometry decode/convert failure reason (type + exception), surfaced into the object's session-log error so
  // a failed curve/point isn't just "did not convert to any native geometry". Receive is single-threaded (main thread).
  private string? _lastDecodeFailure;

  public RhinoHostObjectArtefactBuilder(
    IConverterSettingsStore<RhinoConversionSettings> converterSettings,
    IRootToHostConverter converter,
    IThreadContext threadContext,
    ISdkActivityFactory activityFactory,
    ILogger<RhinoHostObjectArtefactBuilder> logger
  )
  {
    _converterSettings = converterSettings;
    _converter = converter;
    _threadContext = threadContext;
    _activityFactory = activityFactory;
    _logger = logger;
  }

  public Task<HostObjectBuilderResult> Build(
    ArtefactBundle bundle,
    string projectName,
    string modelName,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    // All Rhino document mutation happens on the main thread in one hop (no awaits inside → no sync-over-async deadlock).
    return _threadContext.RunOnMain(() =>
      BakeAll(bundle, projectName, modelName, onOperationProgressed, cancellationToken)
    );
  }

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
    var baseLayerName = $"Project {projectName}: Model {modelName}";
    using var activity = _activityFactory.Start("Build (artefact)");

    // Per-session diagnostics (per-object timing/failures, phase timings, bundle stats) → %TEMP%\Speckle\sessions\.
    using var session = ArtefactSessionLog.Start(
      "Rhino",
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
    var objByGeom = rels.ObjectByGeometry();
    // ObjectByGeometry only covers DISPLAY edges, so it can't reach a block-definition member (a member has no
    // render edge by design). The graph-native join (DEFINES_MEMBER 25 / PLACES 24) inverts that missing direction
    // on the object plane — member↔geometry joins on (definition, member ordinal), immune to the content-hash-dedup
    // collision a geometry-K-keyed inversion cannot distinguish. Bundles predating the vocab (no rel 25) fall back
    // to the @speckle.* member stamps [ENG-9110].
    var memberIndex = TryBuildMemberIndexFromRels(rels) ?? DefinitionMemberStamps.Read(bundle.Properties);
    session.SetStat("memberStamps", memberIndex.ObjectByGeometry.Count + memberIndex.ObjectByInstance.Count);
    var bakedObjectIds = new HashSet<string>();
    var conversionResults = new HashSet<ReceiveConversionResult>();
    // objK → its baked Rhino guids, tracked only for grouped objects (IN_GROUP) so step 5 can rebuild native groups.
    var bakedGuidsByObjK = new Dictionary<int, List<Guid>>();

    using var noDraw = new DisableRedrawScope(doc.Views);

    // 0 - clean previous receive of this model
    DeepClean(doc, baseLayerName);

    // 1 - layers: built lazily per object from the default scene view (e.g. Revit: Model→Level→Category→Family;
    //     Rhino: the nested IN_COLLECTION tree). 2 - materials (MATERIAL nodes) straight into the Rhino tables.
    int baseLayerIndex = EnsureBaseLayer(doc, baseLayerName);
    var layerCache = new Dictionary<string, int>(StringComparer.Ordinal);
    onOperationProgressed.Report(new("Converting materials", null));
    Dictionary<string, Guid> materialByObject;
    Dictionary<int, Guid> materialByGeometry;
    Dictionary<int, Guid> materialByInstance;
    using (session.Phase("Materials"))
    {
      (materialByObject, materialByGeometry, materialByInstance) = CreateMaterials(doc, bundle, objByGeom);
    }

    // By-object display colours (HAS_COLOR → COLOR node), resolved to owning object like materials. appId → argb.
    var colorByObject = CreateColors(bundle, objByGeom);

    // 3 - atomic geometry (objects with a direct DISPLAY/SOLID). Instances + non-geometric elements handled below.
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
          // instance placement (handled in step 4) or a non-geometric element (room/level/area) → skip, don't error.
          if (!rels.DisplayInstanceByObject.ContainsKey(objK))
          {
            session.Increment("nonGeometricSkipped");
          }
          continue;
        }

        var source = Source(appId);
        var sw = Stopwatch.StartNew();
        try
        {
          int layerIndex = ResolveLayer(doc, bundle, objK, baseLayerIndex, layerCache);
          var geometries = DecodeObjectGeometry(objK, bundle, rels, ObjectUnits(bundle, objK));
          if (geometries.Count == 0)
          {
            var reason = _lastDecodeFailure ?? "did not convert to any native geometry";
            session.RecordObject(appId, "Speckle.Object", Status.ERROR, reason, sw.ElapsedMilliseconds);
            conversionResults.Add(new(Status.ERROR, source, null, null, new ConversionException(reason)));
            continue;
          }

          var name = ObjectName(bundle, objK);
          bundle.Properties.TryGetValue(objK, out var objProps);
          // Type Parameters / System Type Parameters, deduped once per type on send (ENG-9136) — resolved here
          // alongside the instance-scoped properties above.
          bundle.TypePropertiesByObject.TryGetValue(objK, out var objTypeProps);
          var ids = new List<Guid>();
          foreach (var (geomK, geom) in geometries)
          {
            if (geom is RG.Hatch hatch)
            {
              // restore pattern/rotation/scale carried as EAV onto the Hatch rebuilt from the SGEO Region
              RhinoHatchStyler.Apply(doc, hatch, objProps, _converterSettings.Current.SpeckleUnits);
            }
            ids.Add(
              BakeObject(
                doc,
                geom,
                geomK,
                layerIndex,
                materialByObject,
                materialByGeometry,
                colorByObject,
                appId,
                name,
                objProps,
                objTypeProps
              )
            );
          }
          bakedObjectIds.UnionWith(ids.Select(g => g.ToString()));
          if (rels.GroupsByObject.ContainsKey(objK))
          {
            bakedGuidsByObjK[objK] = ids;
          }

          // One source object that decoded into several Rhino geometries (a Revit wall's display meshes, say) becomes
          // one native group, so the element stays selectable and movable as a unit [ENG-9113]. The report entry then
          // points at the GROUP id — the selection binding resolves a group id as well as an object id — while
          // bakedObjectIds keeps only the members: a group id in there would make whole-model highlighting walk every
          // object of every group (the same reason the v1 builder kept group ids out of it).
          string reportId = ids[0].ToString();
          if (ids.Count > 1 && GroupElement(doc, ids, name, ObjectType(bundle, objK), appId, baseLayerName) is Guid gid)
          {
            reportId = gid.ToString();
            session.Increment("elementGroupsBaked");
          }
          conversionResults.Add(new(Status.SUCCESS, source, reportId, "Speckle.Object"));
          session.RecordObject(appId, "Speckle.Object", Status.SUCCESS, null, sw.ElapsedMilliseconds);
        }
        catch (Exception ex) when (!ex.IsFatal())
        {
          session.RecordObject(appId, "Speckle.Object", Status.ERROR, ex.Message, sw.ElapsedMilliseconds);
          conversionResults.Add(new(Status.ERROR, source, null, null, ex));
        }
      }
    }

    // 4 - instances: build definitions from DEFINES → geometry, place one per DISPLAY_INSTANCE edge
    if (rels.DisplayInstanceEdges.Count > 0)
    {
      onOperationProgressed.Report(new("Converting instances", null));
      using (session.Phase("Instances"))
      {
        BakeInstances(
          doc,
          bundle,
          rels,
          baseLayerIndex,
          baseLayerName,
          layerCache,
          materialByObject,
          materialByGeometry,
          materialByInstance,
          colorByObject,
          bakedObjectIds,
          bakedGuidsByObjK,
          conversionResults,
          memberIndex,
          session
        );
      }
    }

    // 5 - authored scene groups (IN_GROUP → CONTAINER(Group) nodes) → native Rhino groups.
    if (rels.GroupsByObject.Count > 0)
    {
      using (session.Phase("Groups"))
      {
        BakeGroups(doc, bundle, rels, bakedGuidsByObjK, baseLayerName, session);
      }
    }

    // 6 - named camera viewpoints (envelope.camera_views) → Rhino named views, replacing same-named ones [ENG-9112].
    if (bundle.CameraViews.Count > 0)
    {
      using (session.Phase("Views"))
      {
        RhinoArtefactViewBaker.BakeViews(
          doc,
          bundle.CameraViews,
          _converterSettings.Current.SpeckleUnits,
          session,
          _logger
        );
      }
    }

    doc.Views.Redraw();
    return new HostObjectBuilderResult(bakedObjectIds, conversionResults);
  }

  // ── geometry ──────────────────────────────────────────────────────────────────────────────────────────
  // One decoded Rhino geometry plus the geometry K it came from — carried through so BakeObject can prefer a
  // per-mesh HAS_MATERIAL over the object-level fallback [ENG-9153]. A geomK that decodes to several Rhino
  // geometries (rare one-to-many SGEO conversions) tags every one of them with the SAME geomK, so its material
  // still applies to each result.
  private readonly record struct DecodedGeometry(int GeomK, RG.GeometryBase Geometry);

  private List<DecodedGeometry> DecodeObjectGeometry(
    int objK,
    ArtefactBundle bundle,
    ArtefactRelations rels,
    string fallbackUnits
  )
  {
    _lastDecodeFailure = null;
    var result = new List<DecodedGeometry>();
    var sourceType = ObjectType(bundle, objK); // discriminates a point from a one-point cloud, see AsSourceType
    if (rels.SolidByObject.TryGetValue(objK, out var solidKs))
    {
      foreach (var solidK in solidKs)
      {
        foreach (var geom in DecodeGeometryIndex(solidK, bundle, fallbackUnits, sourceType))
        {
          result.Add(new(solidK, geom));
        }
      }
    }
    if (result.Count == 0 && rels.DisplayByObject(objK) is { } displayEdges)
    {
      foreach (var e in displayEdges.OrderBy(x => x.Ord))
      {
        foreach (var geom in DecodeGeometryIndex(e.Dst, bundle, fallbackUnits, sourceType))
        {
          result.Add(new(e.Dst, geom));
        }
      }
    }
    return result;
  }

  // Decodes one geometry index to Rhino geometry, scaled to doc units (SGEO carries its own units; 3dm uses fallback).
  // <paramref name="sourceType"/> is the owning object's source type, where the primitive alone is ambiguous about the
  // native type to rebuild (see AsSourceType).
  private List<RG.GeometryBase> DecodeGeometryIndex(
    int geomK,
    ArtefactBundle bundle,
    string fallbackUnits,
    string? sourceType = null
  )
  {
    if (!bundle.Geometries.TryGetValue(geomK, out var g))
    {
      return new List<RG.GeometryBase>();
    }

    if (g.Type == RawEncodingFormats.RHINO_3DM)
    {
      var geoms = RawEncodingToHost.Convert3dm(g.Content);
      ApplyUnits(geoms, fallbackUnits);
      return geoms;
    }
    if (g.IsSgeo)
    {
      // The header routes the blob: it says whether this is a mesh at all, and whether that mesh carries per-vertex
      // visualisation data. Read once up front rather than letting TryDecodeMesh read it again for a non-mesh.
      var header = SgeoDecoder.ReadHeader(g.Content);
      // Meshes take the fast hand-rolled path (no Base allocation), scaled here — but only when there are no authored
      // normals or UVs to lose. SgeoMesh carries neither (TryDecodeMesh reads past them to reach the colours), so a
      // mesh sent with "Add Mesh Visualization Properties" goes the long way round instead: the full decoder keeps
      // them and MeshToHostConverter applies them to the Rhino mesh [ENG-9214].
      if (IsFastPathMesh(header) && SgeoDecoder.TryDecodeMesh(g.Content, out var sm))
      {
        var mesh = BuildMesh(sm);
        var list = new List<RG.GeometryBase> { mesh };
        ApplyUnits(list, sm.Units);
        return list;
      }
      // Curves, points, and other primitives: decode to a Speckle geometry object and convert via the Rhino ToHost
      // converter, which already scales to doc units (so no ApplyUnits here). An unsupported primitive degrades to
      // nothing rather than aborting the whole receive.
      Base? decoded = null;
      try
      {
        decoded = AsSourceType(SgeoDecoder.Decode(g.Content), sourceType);
        var converted = ConvertSpeckleGeometry(decoded);
        if (converted.Count == 0)
        {
          // decode + convert both ran without throwing, but produced no bakeable geometry (e.g. a converter returned an
          // unhandled result shape). Record it so it isn't a silent drop.
          _lastDecodeFailure =
            $"geom {geomK} ({g.Type}, {decoded.speckle_type}): converter returned no native geometry";
          _logger.LogWarning("Skipped SGEO geometry {GeomK}: {Reason}", geomK, _lastDecodeFailure);
        }
        return converted;
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        string stage = decoded is null ? "decode" : $"convert of {decoded.speckle_type}";
        _lastDecodeFailure = $"geom {geomK} ({g.Type}) {stage} failed — {ex.GetType().Name}: {ex.Message}";
        _logger.LogWarning(
          ex,
          "Skipped SGEO geometry {GeomK} (type '{Type}', {Bytes} bytes) at {Stage}: {Error}",
          geomK,
          g.Type,
          g.Content.Length,
          stage,
          ex.Message
        );
      }
    }
    return new List<RG.GeometryBase>();
  }

  /// <summary>Rhino's <c>ObjectType</c> for a single point object, as the send side stamps it on the object row
  /// (<c>rhinoObject.ObjectType.ToString()</c>). A point cloud reports <c>PointSet</c> instead.</summary>
  private const string RHINO_POINT_TYPE = "Point";

  // SGEO encodes a single point and a whole point cloud under the same Points primitive, so Decode can only ever hand
  // back a Pointcloud — and a native Rhino point came back as a one-point PointCloud, a different object type to every
  // command, filter and script downstream [ENG-9215]. The object's source type is the discriminator the blob lacks:
  // Rhino stamps its ObjectType on each object row, so a one-point cloud whose object called itself a "Point" is
  // handed to the converter as a Point. A genuine one-point PointSet says "PointSet" and stays a point cloud.
  private static Base AsSourceType(Base decoded, string? sourceType) =>
    string.Equals(sourceType, RHINO_POINT_TYPE, StringComparison.Ordinal)
    && decoded is SOG.Pointcloud { points.Count: 3 } cloud
      ? new SOG.Point(cloud.points[0], cloud.points[1], cloud.points[2], cloud.units)
      : decoded;

  // Speckle geometry object (from SgeoDecoder.Decode) → Rhino geometry via the ToHost converter. The top-level converter
  // returns a single GeometryBase for primitives (curve/point/…) or a list for one-to-many cases; both are unwrapped.
  private List<RG.GeometryBase> ConvertSpeckleGeometry(Base decoded)
  {
    var converted = _converter.Convert(decoded);
    return converted switch
    {
      RG.GeometryBase gb => new List<RG.GeometryBase> { gb },
      IEnumerable<RG.GeometryBase> many => many.ToList(),
      _ => new List<RG.GeometryBase>(),
    };
  }

  private static void ApplyUnits(List<RG.GeometryBase> geoms, string? units)
  {
    var settings = RhinoDoc.ActiveDoc;
    if (settings is null || units is not { Length: > 0 } u)
    {
      return;
    }
    var docUnits = settings.ModelUnitSystem.ToSpeckleString();
    if (string.Equals(u, docUnits, StringComparison.OrdinalIgnoreCase))
    {
      return;
    }
    var t = RG.Transform.Scale(RG.Point3d.Origin, Units.GetConversionFactor(u, docUnits));
    foreach (var geom in geoms)
    {
      geom.Transform(t);
    }
  }

  // True for a mesh blob the hand-rolled BuildMesh can rebuild losslessly: normals and UVs are the two things SgeoMesh
  // doesn't carry, so a blob with either has to go through the full decoder instead [ENG-9214]. n-gons are safe here —
  // they ride the face array, and BuildMesh rebuilds their MeshNgon records.
  private static bool IsFastPathMesh(SgeoHeader header) =>
    header.PrimitiveType == SgeoPrimitiveType.Mesh && (header.Flags & (SgeoFlags.HasNormals | SgeoFlags.HasUvs)) == 0;

  // SGEO neutral mesh → Rhino mesh (Speckle count-prefixed face format; matches MeshToHostConverter).
  private static RG.Mesh BuildMesh(SgeoMesh sm)
  {
    var mesh = new RG.Mesh();
    var v = sm.Vertices;
    for (int i = 0; i + 2 < v.Length; i += 3)
    {
      mesh.Vertices.Add(v[i], v[i + 1], v[i + 2]);
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
        mesh.Faces.AddFace(f[p + 1], f[p + 2], f[p + 3]);
      }
      else if (n == 4 && p + 4 < f.Length)
      {
        mesh.Faces.AddFace(f[p + 1], f[p + 2], f[p + 3], f[p + 4]);
      }
      else if (n > 4 && p + n < f.Length)
      {
        // n-gon: fan-triangulate into the face table, then record the MeshNgon over exactly those faces so the source
        // polygon survives as one face to Rhino's eyes (Explode, _SelNgon, re-send) instead of loose triangles
        // [ENG-9214]. Same reconstruction MeshToHostConverter does on the Base path.
        var ngonFaces = new List<int>(n - 2);
        for (int k = 1; k < n - 1; k++)
        {
          ngonFaces.Add(mesh.Faces.AddFace(f[p + 1], f[p + 1 + k], f[p + 2 + k]));
        }
        var ngonVertices = new int[n];
        for (int k = 0; k < n; k++)
        {
          ngonVertices[k] = f[p + 1 + k];
        }
        mesh.Ngons.AddNgon(RG.MeshNgon.Create(ngonVertices, ngonFaces));
      }
      else
      {
        break;
      }
      p += n + 1;
    }

    if (sm.Colors.Length == mesh.Vertices.Count && sm.Colors.Length > 0)
    {
      foreach (var argb in sm.Colors)
      {
        mesh.VertexColors.Add(Color.FromArgb(argb));
      }
    }
    mesh.Normals.ComputeNormals();
    // Compact() only trims capacity and culls unreferenced vertices — of which an SGEO mesh has none, its vertex array
    // being exactly the source's. Skip it once n-gons are in play rather than run their fresh records through its
    // reindex for no gain [ENG-9214].
    if (mesh.Ngons.Count == 0)
    {
      mesh.Compact();
    }
    return mesh;
  }

  private Guid BakeObject(
    RhinoDoc doc,
    RG.GeometryBase geom,
    int geomK,
    int layerIndex,
    Dictionary<string, Guid> materialByObject,
    Dictionary<int, Guid> materialByGeometry,
    Dictionary<string, int> colorByObject,
    string appId,
    string? name,
    Dictionary<string, object?>? properties,
    Dictionary<string, object?>? typeProperties
  )
  {
    var atts = new ObjectAttributes { LayerIndex = layerIndex };
    if (name is { Length: > 0 })
    {
      atts.Name = name;
    }
    // source properties (Rhino user text / user dictionaries, Revit + IFC parameters) → user strings [ENG-9111].
    // Type-scoped first, instance-scoped second, so a colliding key is won by the instance value — matching
    // Revit's own precedence [ENG-9136].
    RhinoArtefactUserStrings.Apply(atts, typeProperties);
    RhinoArtefactUserStrings.Apply(atts, properties);
    // Prefer this mesh's own HAS_MATERIAL over the object-level fallback, so an object with several differently
    // materialled display meshes (e.g. a multi-material Revit wall) keeps each mesh's own material instead of
    // collapsing them all to whichever relation was resolved last [ENG-9153].
    if (
      materialByGeometry.TryGetValue(geomK, out Guid materialGuid)
      || materialByObject.TryGetValue(appId, out materialGuid)
    )
    {
      atts.RenderMaterial = RenderContent.FromId(doc, materialGuid) as RhinoRenderMaterial;
      atts.MaterialSource = ObjectMaterialSource.MaterialFromObject;
    }
    if (colorByObject.TryGetValue(appId, out int argb))
    {
      atts.ObjectColor = Color.FromArgb(argb);
      atts.ColorSource = ObjectColorSource.ColorFromObject; // by-object display colour (HAS_COLOR)
    }
    return doc.Objects.Add(geom, atts);
  }

  // Binds the several Rhino geometries decoded from ONE source object into a native group [ENG-9113]. The name carries
  // the baseLayerName suffix — like BakeGroups and the v1 fallback grouping — so DeepClean purges it on re-receive.
  // The source appId is in the name to keep it unique between two same-named elements. Returns null when Rhino
  // refused the group, so the caller falls back to reporting the first member.
  private static Guid? GroupElement(
    RhinoDoc doc,
    List<Guid> ids,
    string? name,
    string? type,
    string appId,
    string baseLayerName
  )
  {
    var label =
      name is { Length: > 0 } n ? n
      : type is { Length: > 0 } t ? t
      : "Element";
    int index = doc.Groups.Add($"{label} - {appId} ({baseLayerName})", ids);
    return index < 0 ? null : doc.Groups.FindIndex(index)?.Id;
  }

  // ── instances ─────────────────────────────────────────────────────────────────────────────────────────
#pragma warning disable CA1506
  private void BakeInstances(
#pragma warning restore CA1506
    RhinoDoc doc,
    ArtefactBundle bundle,
    ArtefactRelations rels,
    int baseLayerIndex,
    string baseLayerName,
    Dictionary<string, int> layerCache,
    Dictionary<string, Guid> materialByObject,
    Dictionary<int, Guid> materialByGeometry,
    Dictionary<int, Guid> materialByInstance,
    Dictionary<string, int> colorByObject,
    HashSet<string> bakedObjectIds,
    Dictionary<int, List<Guid>> bakedGuidsByObjK,
    HashSet<ReceiveConversionResult> conversionResults,
    DefinitionMemberIndex memberIndex,
    ArtefactSessionLog session
  )
  {
    var docUnits = _converterSettings.Current.SpeckleUnits;

    // definitions (incl. nested blocks): a DEFINITION node owns its geometry directly and may contain nested placements.
    var defIndexByNode = BuildDefinitions(
      doc,
      bundle,
      rels,
      materialByGeometry,
      materialByInstance,
      docUnits,
      baseLayerName,
      baseLayerIndex,
      layerCache,
      memberIndex,
      session
    );

    // placements: one instance per DISPLAY_INSTANCE edge (object → INSTANCE node); an object may place several.
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
      var source = Source(appId);
      var sw = Stopwatch.StartNew();
      if (instNode.DefRef is not int defNodeK || !defIndexByNode.TryGetValue(defNodeK, out int defIndex))
      {
        session.RecordObject(
          appId,
          "Instance (Block)",
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
            new ConversionException("Instance references a definition with no geometry")
          )
        );
        continue;
      }

      var transform = BuildTransform(instNode.Transform, instNode.Units is { Length: > 0 } u ? u : docUnits, docUnits);
      int layerIndex = ResolveLayer(doc, bundle, objK, baseLayerIndex, layerCache);
      var atts = new ObjectAttributes { LayerIndex = layerIndex };
      if (ObjectName(bundle, objK) is { Length: > 0 } instName)
      {
        atts.Name = instName;
      }
      // the placement's own properties → user strings, same as an atomic object [ENG-9111]. Type-scoped first,
      // instance-scoped second, so a colliding key is won by the instance value [ENG-9136].
      bundle.Properties.TryGetValue(objK, out var instProps);
      bundle.TypePropertiesByObject.TryGetValue(objK, out var instTypeProps);
      RhinoArtefactUserStrings.Apply(atts, instTypeProps);
      RhinoArtefactUserStrings.Apply(atts, instProps);
      // Prefer a material painted directly on THIS placement (instance-sourced HAS_MATERIAL) over the object-level
      // fallback, so a per-instance override survives instead of always rendering the definition's own material
      // [ENG-9109].
      if (
        materialByInstance.TryGetValue(instNodeK, out Guid materialGuid)
        || materialByObject.TryGetValue(appId, out materialGuid)
      )
      {
        atts.RenderMaterial = RenderContent.FromId(doc, materialGuid) as RhinoRenderMaterial;
        atts.MaterialSource = ObjectMaterialSource.MaterialFromObject;
      }
      // the placement's own colour (object-sourced HAS_COLOR), so a per-instance override survives [ENG-9114]
      if (colorByObject.TryGetValue(appId, out int instArgb))
      {
        atts.ObjectColor = Color.FromArgb(instArgb);
        atts.ColorSource = ObjectColorSource.ColorFromObject;
      }

      var id = doc.Objects.AddInstanceObject(defIndex, transform, atts);
      if (id == Guid.Empty)
      {
        session.RecordObject(
          appId,
          "Instance (Block)",
          Status.ERROR,
          "AddInstanceObject failed",
          sw.ElapsedMilliseconds
        );
        conversionResults.Add(
          new(Status.ERROR, source, null, null, new ConversionException("Failed to place instance"))
        );
        continue;
      }
      bakedObjectIds.Add(id.ToString());
      if (rels.GroupsByObject.ContainsKey(objK))
      {
        if (!bakedGuidsByObjK.TryGetValue(objK, out var grouped))
        {
          bakedGuidsByObjK[objK] = grouped = new List<Guid>();
        }
        grouped.Add(id); // an object may place several instances; all of them join its group(s)
      }
      conversionResults.Add(new(Status.SUCCESS, source, id.ToString(), "Instance (Block)"));
      session.RecordObject(appId, "Instance (Block)", Status.SUCCESS, null, sw.ElapsedMilliseconds);
    }
  }

  // Groups a definition's DEFINES geometry Ks by member ordinal (index-aligned with ords), then within each member
  // prefers the authoritative 3dm solid over its display mesh(es); a member with no solid yields all its geometry.
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
        .Where(k => bundle.Geometries.TryGetValue(k, out var g) && g.Type == RawEncodingFormats.RHINO_3DM)
        .ToList();
      yield return solids.Count > 0 ? solids : geoms;
    }
  }

  // The rel-built member index: DEFINES_MEMBER (25, def → member object, ord = member ordinal) joined against the
  // definition's DEFINES ords recovers geometry K → member object K; PLACES (24, member object → INSTANCE node)
  // inverts to INSTANCE K → member object K. Returns null when the bundle predates the vocabulary (no rel 25 rows)
  // so the caller falls back to the legacy @speckle.* stamps.
  private static DefinitionMemberIndex? TryBuildMemberIndexFromRels(ArtefactRelations rels)
  {
#if SDK_BUNDLE_VOCAB_ADDITIONS
    // Requires Speckle.Objects ≥ speckle-sharp-sdk@oguzhan/bundle-vocab-additions:
    //   rels.DefinesMemberByDefinition : Dictionary<int, List<ArtefactEdge>>  (def K → member edges, Ord = member ordinal)
    //   rels.PlacesByObject            : Dictionary<int, int>                 (member object K → INSTANCE node K)
    if (rels.DefinesMemberByDefinition.Count == 0)
    {
      return null;
    }
    var byGeometry = new Dictionary<int, int>();
    var byInstance = new Dictionary<int, int>();
    foreach (var kv in rels.DefinesMemberByDefinition)
    {
      // member ordinal → member object K for this definition
      var objByOrd = new Dictionary<int, int>();
      foreach (var e in kv.Value)
      {
        objByOrd[e.Ord] = e.Dst;
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
#else
    // No-op until the SDK vocab pin bump — the pinned ArtefactBundle reader drops unknown rels, so rel 25/24
    // rows are not even surfaced yet. Define SDK_BUNDLE_VOCAB_ADDITIONS after bumping Speckle.Objects to a build
    // of speckle-sharp-sdk@oguzhan/bundle-vocab-additions.
    _ = rels;
    return null;
#endif
  }

  // Builds every DEFINITION node into a Rhino InstanceDefinition, returning node K → Rhino defIndex. A DEFINITION owns
  // its geometry directly (DEFINES → geometry blobs) and may also contain nested block placements (DEFINES_INSTANCE →
  // INSTANCE node). Nested definitions are built depth-first (memoized in defIndexByNode) so a parent can reference the
  // child definition's Rhino Guid via an InstanceReferenceGeometry member carrying the nested placement's own transform.
  // The geometry's material (HAS_MATERIAL → geometry) is baked onto the members so placed instances aren't all grey,
  // and each member's own LAYER is recovered through its stamp → object row → IN_COLLECTION [ENG-9110]; the rest of
  // the member's attributes (name, user strings, colour) ride the same join [ENG-9213].
  private Dictionary<int, int> BuildDefinitions(
    RhinoDoc doc,
    ArtefactBundle bundle,
    ArtefactRelations rels,
    Dictionary<int, Guid> materialByGeometry,
    Dictionary<int, Guid> materialByInstance,
    string docUnits,
    string baseLayerName,
    int baseLayerIndex,
    Dictionary<string, int> layerCache,
    DefinitionMemberIndex memberIndex,
    ArtefactSessionLog session
  )
  {
    var defIndexByNode = new Dictionary<int, int>();
    var defBuilding = new HashSet<int>();

    int BuildDefinition(int defNodeK)
    {
      if (defIndexByNode.TryGetValue(defNodeK, out int already))
      {
        return already; // already built (a shared nested definition is reached from several parents)
      }
      if (!bundle.Nodes.TryGetValue(defNodeK, out var defNode) || defNode.Kind != NodeKind.Definition)
      {
        return -1;
      }
      if (!defBuilding.Add(defNodeK))
      {
        return -1; // cycle guard — Rhino disallows recursive block definitions; never stack-overflow on a bad bundle
      }
      session.Increment("definitionsSeen");

      var geometryList = new List<RG.GeometryBase>();
      var attributeList = new List<ObjectAttributes>();

      // direct geometry members (DEFINES → geometry blob). A member's geometry shares a member ordinal; within each
      // member we prefer the authoritative 3dm solid over its display mesh(es), so a solid inside a block rebuilds as a
      // solid, not a mesh. A member with no solid (plain mesh/curve/point) keeps all its geometry.
      if (rels.DefinesByDefinition.TryGetValue(defNodeK, out var geomKs))
      {
        rels.DefinesOrdByDefinition.TryGetValue(defNodeK, out var ords);
        foreach (var memberGeomKs in GroupDefinesByMember(geomKs, ords, bundle))
        {
          foreach (var geomK in memberGeomKs)
          {
            // Definition geometry is in the model's source units; the raw-3dm path rescales from this fallback to the
            // doc units. Pass the bundle (source) units, NOT docUnits — otherwise a 3dm member isn't rescaled and a
            // block sent from a metre model lands 1000x too small / mispositioned in a millimetre doc.
            // The member's own source type rides the same stamp join as its attributes, so a point inside a block
            // rebuilds as a native point rather than a one-point cloud [ENG-9215].
            var memberType = memberIndex.ObjectByGeometry.TryGetValue(geomK, out int memberObjK)
              ? ObjectType(bundle, memberObjK)
              : null;
            var decoded = DecodeGeometryIndex(geomK, bundle, bundle.Units, memberType);
            materialByGeometry.TryGetValue(geomK, out Guid mg);
            foreach (var geom in decoded)
            {
              geometryList.Add(geom);
              // The member's own layer, name, user strings and colour, all recovered through its geometry stamp →
              // member object row. A direct member's colour is geometry-sourced, so its geometry K is the colour key.
              attributeList.Add(
                BuildMemberAttributes(
                  doc,
                  bundle,
                  memberIndex.ObjectByGeometry,
                  geomK,
                  geomK,
                  mg,
                  baseLayerIndex,
                  layerCache
                )
              );
            }
          }
        }
      }

      // nested block members (DEFINES_INSTANCE → INSTANCE node): build the child definition first (depth-first) and
      // add an InstanceReferenceGeometry that references it with the nested placement's own transform.
      if (rels.DefinesInstanceByDefinition.TryGetValue(defNodeK, out var nestedInstNodeKs))
      {
        foreach (var instNodeK in nestedInstNodeKs)
        {
          if (!bundle.Nodes.TryGetValue(instNodeK, out var nestedInst) || nestedInst.DefRef is not int childDefNodeK)
          {
            continue;
          }
          int childDefIndex = BuildDefinition(childDefNodeK);
          if (childDefIndex < 0)
          {
            session.Increment("nestedInstancesUnresolved");
            continue;
          }
          var childDefId = doc.InstanceDefinitions[childDefIndex].Id;
          var nestedTransform = BuildTransform(
            nestedInst.Transform,
            nestedInst.Units is { Length: > 0 } u ? u : docUnits,
            docUnits
          );
          // A nested-block member owns no geometry K, so its whole join — layer, name, user strings, colour — hangs
          // off its INSTANCE node K instead. Its material is instance-sourced (HAS_MATERIAL ord=1) for the same
          // reason, and its colour is object-sourced (HAS_COLOR ord=1), so no geometry K goes in [ENG-9213].
          materialByInstance.TryGetValue(instNodeK, out Guid nestedMaterial);
          var nestedAtts = BuildMemberAttributes(
            doc,
            bundle,
            memberIndex.ObjectByInstance,
            instNodeK,
            null,
            nestedMaterial,
            baseLayerIndex,
            layerCache
          );
          geometryList.Add(new RG.InstanceReferenceGeometry(childDefId, nestedTransform));
          attributeList.Add(nestedAtts);
          session.Increment("nestedInstancesPlaced");
        }
      }

      defBuilding.Remove(defNodeK);

      if (geometryList.Count == 0)
      {
        session.Increment("definitionsEmpty");
        return -1;
      }
      // The baseLayerName suffix scopes the generated name to this model card, so DeepClean can purge exactly the
      // definitions the previous receive of THIS card created and leave other cards' and user-authored blocks alone
      // [ENG-9115]. Same convention as the group names in BakeGroups.
      var defName = RhinoUtils.CleanBlockDefinitionName(
        $"{defNode.Name ?? "Definition"}-(def-{defNodeK}) ({baseLayerName})"
      );
      int defIndex = doc.InstanceDefinitions.Add(defName, "", RG.Point3d.Origin, geometryList, attributeList);
      if (defIndex < 0)
      {
        session.Increment("definitionsEmpty");
        return -1;
      }
      defIndexByNode[defNodeK] = defIndex;
      return defIndex;
    }

    foreach (var kv in bundle.Nodes)
    {
      if (kv.Value.Kind == NodeKind.Definition)
      {
        BuildDefinition(kv.Key);
      }
    }

    return defIndexByNode;
  }

  // ── groups ────────────────────────────────────────────────────────────────────────────────────────────
  // Authored scene groups: inverts GroupsByObject (object → its CONTAINER(Group) nodes) to group → member guids and
  // adds one native Rhino group per node. An overlapping/nested source group works out of the box — Rhino models
  // nesting the same way (an object simply belongs to several groups). The name carries the baseLayerName suffix
  // (like the v1 RhinoGroupBaker) so DeepClean purges this model's groups on re-receive. A member that failed to
  // bake (or is non-geometric) is skipped; the group keeps its other members. Smaller groups first, as in v1.
  private static void BakeGroups(
    RhinoDoc doc,
    ArtefactBundle bundle,
    ArtefactRelations rels,
    Dictionary<int, List<Guid>> bakedGuidsByObjK,
    string baseLayerName,
    ArtefactSessionLog session
  )
  {
    var membersByGroup = new Dictionary<int, List<Guid>>();
    foreach (var kv in rels.GroupsByObject)
    {
      if (!bakedGuidsByObjK.TryGetValue(kv.Key, out var guids))
      {
        continue;
      }
      foreach (int groupK in kv.Value)
      {
        if (!membersByGroup.TryGetValue(groupK, out var list))
        {
          membersByGroup[groupK] = list = new List<Guid>();
        }
        list.AddRange(guids);
      }
    }

    foreach (var kv in membersByGroup.OrderBy(g => g.Value.Count))
    {
      bundle.Nodes.TryGetValue(kv.Key, out var node);
      var name = node?.Name is { Length: > 0 } n ? n : "Group";
      doc.Groups.Add($"{name} ({baseLayerName})", kv.Value);
      session.Increment("groupsBaked");
    }
  }

  // ── layers ────────────────────────────────────────────────────────────────────────────────────────────
  private static int EnsureBaseLayer(RhinoDoc doc, string baseLayerName)
  {
    var existing = doc.Layers.FindName(baseLayerName);
    return existing?.Index ?? doc.Layers.Add(new Layer { Name = baseLayerName });
  }

  // Resolves an object's layer from the default scene view (e.g. Model→Level→Category→Family), creating the nested
  // Rhino layers on demand. Falls back to the base layer when the bundle has no scene view (or the object matches no tier).
  private static int ResolveLayer(
    RhinoDoc doc,
    ArtefactBundle bundle,
    int objK,
    int baseLayerIndex,
    Dictionary<string, int> layerCache
  )
  {
    // Host-agnostic scene-view → grouping segments lives in the SDK (SceneViewResolver) so every connector reuses it.
    var segments = SceneViewResolver.SegmentsWithColor(bundle, objK); // (name, argb) so layers get their source colour
    return segments.Count == 0 ? baseLayerIndex : GetOrCreateLayer(doc, segments, baseLayerIndex, layerCache);
  }

  // A block-definition member's layer. The member has its own object row carrying the ordinary object-sourced
  // IN_COLLECTION, but nothing reaches that row from the definition side — a member has no DISPLAY edge, so
  // ObjectByGeometry() can't invert it. The DefinitionMemberStamps eav join supplies the missing direction:
  // geometry K (or nested INSTANCE node K) → member object K → the SAME ResolveLayer every top-level object uses,
  // so a layer shared by a member and a scene object is created once (shared layerCache). Unstamped → base layer.
  private static int ResolveMemberLayer(
    RhinoDoc doc,
    ArtefactBundle bundle,
    IReadOnlyDictionary<int, int> memberObjectByK,
    int k,
    int baseLayerIndex,
    Dictionary<string, int> layerCache
  ) =>
    memberObjectByK.TryGetValue(k, out int memberObjK)
      ? ResolveLayer(doc, bundle, memberObjK, baseLayerIndex, layerCache)
      : baseLayerIndex;

  // ── definition members ────────────────────────────────────────────────────────────────────────────────
  /// <summary>
  /// One definition member's Rhino attributes. The member's object row carries everything a top-level object's does —
  /// layer [ENG-9110], name, user strings / EAV properties and by-object colour — and the same stamp join reaches all
  /// of it, so a member is no longer rebuilt as a bare layer+material shell [ENG-9213].
  /// </summary>
  /// <param name="memberObjectByK">The stamp index for this member's K-space: geometry for a direct member,
  /// INSTANCE node for a nested-block member.</param>
  /// <param name="k">The member's key inside <paramref name="memberObjectByK"/>.</param>
  /// <param name="geometryK">The member's geometry K when it has one, else null. Geometry and INSTANCE node Ks
  /// overlap numerically, so this must stay null for a nested member or its colour would be looked up in the wrong
  /// K-space and could hit an unrelated geometry's HAS_COLOR.</param>
  /// <param name="materialGuid">Resolved by the caller — geometry-sourced (ord=0) for a direct member,
  /// instance-sourced (ord=1) for a nested placement. <see cref="Guid.Empty"/> leaves the material by-layer.</param>
  private static ObjectAttributes BuildMemberAttributes(
    RhinoDoc doc,
    ArtefactBundle bundle,
    IReadOnlyDictionary<int, int> memberObjectByK,
    int k,
    int? geometryK,
    Guid materialGuid,
    int baseLayerIndex,
    Dictionary<string, int> layerCache
  )
  {
    // ObjectAttributes is IDisposable but InstanceDefinitions.Add takes ownership — disposing here would corrupt the
    // definition; the doc owns them for the document lifetime.
#pragma warning disable CA2000
    var atts = new ObjectAttributes
    {
      LayerIndex = ResolveMemberLayer(doc, bundle, memberObjectByK, k, baseLayerIndex, layerCache),
    };
#pragma warning restore CA2000
    if (materialGuid != Guid.Empty)
    {
      atts.RenderMaterial = RenderContent.FromId(doc, materialGuid) as RhinoRenderMaterial;
      atts.MaterialSource = ObjectMaterialSource.MaterialFromObject;
    }
    if (!memberObjectByK.TryGetValue(k, out int memberObjK))
    {
      return atts; // unstamped (pre-ENG-9110 bundle) → layer + material only, exactly what members got before
    }

    if (ObjectName(bundle, memberObjK) is { Length: > 0 } name)
    {
      atts.Name = name;
    }
    // Type-scoped first, instance-scoped second, so a colliding key is won by the instance value — the same
    // precedence BakeObject applies to a top-level object [ENG-9136].
    bundle.Properties.TryGetValue(memberObjK, out var props);
    bundle.TypePropertiesByObject.TryGetValue(memberObjK, out var typeProps);
    RhinoArtefactUserStrings.Apply(atts, typeProps);
    RhinoArtefactUserStrings.Apply(atts, props);
    if (MemberColor(bundle, memberObjK, geometryK) is int argb)
    {
      atts.ObjectColor = Color.FromArgb(argb);
      atts.ColorSource = ObjectColorSource.ColorFromObject;
    }
    return atts;
  }

  // A member's by-object colour: geometry-sourced (HAS_COLOR ord=0) for a direct member, object-sourced (ord=1) for a
  // nested placement — the same two shapes CreateColors handles, except CreateColors resolves through
  // ObjectByGeometry, which is built from the DISPLAY edges a member deliberately never has.
  private static int? MemberColor(ArtefactBundle bundle, int memberObjK, int? geometryK)
  {
    var rels = bundle.Relations;
    bool found =
      (geometryK is int gk && rels.ColorByGeometry.TryGetValue(gk, out int colorNodeK))
      || rels.ColorByObject.TryGetValue(memberObjK, out colorNodeK);
    return found && bundle.Nodes.TryGetValue(colorNodeK, out var node) && node.Kind == NodeKind.Color
      ? node.Argb
      : null;
  }

  // Creates (or reuses) the nested layer chain for the given segments under the base layer; returns the leaf index.
  private static int GetOrCreateLayer(
    RhinoDoc doc,
    IReadOnlyList<(string Name, int? Argb)> segments,
    int baseLayerIndex,
    Dictionary<string, int> cache
  )
  {
    int parentIndex = baseLayerIndex;
    var soFar = new List<string>();
    foreach (var (raw, argb) in segments)
    {
      var name = RhinoUtils.CleanLayerName(string.IsNullOrWhiteSpace(raw) ? "unnamed" : raw);
      soFar.Add(name);
      var key = string.Join("", soFar);
      if (cache.TryGetValue(key, out int existing))
      {
        parentIndex = existing;
        continue;
      }
      var layer = new Layer { Name = name, ParentLayerId = doc.Layers[parentIndex].Id };
      if (argb is int a)
      {
        layer.Color = Color.FromArgb(a); // the layer's source colour, carried on its CONTAINER node's argb
      }
      int idx = doc.Layers.Add(layer);
      cache[key] = idx;
      parentIndex = idx;
    }
    return parentIndex;
  }

  // ── materials ─────────────────────────────────────────────────────────────────────────────────────────
  private (
    Dictionary<string, Guid> byObject,
    Dictionary<int, Guid> byGeometry,
    Dictionary<int, Guid> byInstance
  ) CreateMaterials(RhinoDoc doc, ArtefactBundle bundle, Dictionary<int, int> objByGeom)
  {
    var guidByMaterialNode = new Dictionary<int, Guid>();
    // Render materials this receive already created, by name. Nothing else in the receive is name-owned by accident:
    // Speckle's material identity IS the name (the send side keys its proxies by it and the unpacker reads it back), so
    // a name hit on re-receive is this operation's own material. Without this every run added a fresh copy of every
    // material node and the table grew without bound, since DeepClean purges objects/layers/definitions but not
    // materials [ENG-9217]. Indexed once — the table is walked per node otherwise.
    var existingByName = new Dictionary<string, RhinoRenderMaterial>(StringComparer.Ordinal);
    foreach (var existingMaterial in doc.RenderMaterials)
    {
      if (existingMaterial?.Name is { Length: > 0 } existingName && !existingByName.ContainsKey(existingName))
      {
        existingByName[existingName] = existingMaterial; // first wins, as the legacy baker's FirstOrDefault did
      }
    }

    foreach (var kv in bundle.Nodes)
    {
      var n = kv.Value;
      if (n.Kind != NodeKind.Material)
      {
        continue;
      }
      try
      {
        var matName = (n.Name ?? "material").Replace("[", "").Replace("]", "");
        var rhinoMaterial = new Material
        {
          Name = matName,
          DiffuseColor = Color.FromArgb(n.Argb ?? unchecked((int)0xFFFFFFFF)),
          Transparency = 1 - (n.Opacity ?? 1.0),
        };
        // Legacy channels first (v1 RhinoMaterialBaker parity) so the basic-material fallback below still carries them.
        if (n.Emissive is int emissive)
        {
          rhinoMaterial.EmissionColor = Color.FromArgb(emissive);
        }
        if (n.Ior is double ior)
        {
          rhinoMaterial.IndexOfRefraction = ior;
        }

        // ENG-8791 receive half: bake as a Physically Based material so the bundle's metalness/roughness apply
        // (CreateBasicMaterial ignores them) and a re-send finds the PBR channels where the unpacker reads them.
        rhinoMaterial.ToPhysicallyBased();
        var pbr = rhinoMaterial.PhysicallyBased;
        if (pbr is not null)
        {
          pbr.Metallic = n.Metalness ?? 0.0;
          pbr.Roughness = n.Roughness ?? 1.0;
        }

        if (existingByName.TryGetValue(matName, out var existing))
        {
          if (MaterialMatches(existing, rhinoMaterial))
          {
            // Unchanged version re-received: keep the material AND its id, so every object still pointing at it
            // (another model card's, or one the user assigned by hand) keeps rendering.
            guidByMaterialNode[kv.Key] = existing.Id;
            continue;
          }
          // The version's material moved on. Drop the stale one so the refreshed material can take its name back
          // instead of the two of them piling up.
          doc.RenderMaterials.Remove(existing);
          existingByName.Remove(matName);
        }

        // FromMaterial returns null on headless docs (importer) — same fallback as RhinoMaterialUnpacker.
        var renderMaterial =
          RhinoRenderMaterial.FromMaterial(rhinoMaterial, doc)
          ?? RhinoRenderMaterial.CreateBasicMaterial(rhinoMaterial, doc);
        doc.RenderMaterials.Add(renderMaterial);
        // Two MATERIAL nodes can carry the same name (identity is the name), so record it: the second reuses this
        // material rather than adding its twin.
        existingByName[matName] = renderMaterial;
        guidByMaterialNode[kv.Key] = renderMaterial.Id;
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogError(ex, "Failed to create artefact render material for node {Node}", kv.Key);
      }
    }

    var byObject = new Dictionary<string, Guid>();
    var byGeometry = new Dictionary<int, Guid>();
    foreach (var kv in bundle.Relations.MaterialByGeometry)
    {
      if (!guidByMaterialNode.TryGetValue(kv.Value, out Guid guid))
      {
        continue;
      }
      byGeometry[kv.Key] = guid; // geometry → material (covers standalone definition geometry)
      if (objByGeom.TryGetValue(kv.Key, out int objK) && bundle.ObjectAppIds.TryGetValue(objK, out var appId))
      {
        byObject[appId] = guid; // object → material (atomic display objects)
      }
    }

    // Instance-sourced (ord=1): a material painted directly on a block placement (Rhino MaterialFromObject on
    // the instance itself), keyed by the placement's INSTANCE node K rather than any geometry it owns — a
    // placement owns no geometry of its own, so it's invisible to the geometry loop above [ENG-9109].
    var byInstance = new Dictionary<int, Guid>();
    foreach (var kv in bundle.Relations.MaterialByInstance)
    {
      if (guidByMaterialNode.TryGetValue(kv.Value, out Guid guid))
      {
        byInstance[kv.Key] = guid;
      }
    }
    return (byObject, byGeometry, byInstance);
  }

  // True when an already-present render material still carries the values this bundle asks for, i.e. re-receiving hasn't
  // changed it — the case worth keeping the existing id alive for. Compared against the material's simulated form, the
  // only shape a RenderContent's values can be read back in; a mismatch it can't represent (a non-PBR simulation where
  // a PBR channel is expected, say) reads as "changed", which costs a rebuild of the material but never a duplicate.
  // SimulateMaterial(ref, …) rather than the tidier SimulatedMaterial/ToMaterial: it is the one overload that is
  // current in both Rhino 7 and Rhino 8, and this file compiles for both.
  private static bool MaterialMatches(RhinoRenderMaterial existing, Material desired)
  {
    var simulated = new Material();
    try
    {
      existing.SimulateMaterial(ref simulated, RenderTexture.TextureGeneration.Disallow);
      var simulatedPbr = simulated.PhysicallyBased;
      var desiredPbr = desired.PhysicallyBased;
      return simulated.DiffuseColor.ToArgb() == desired.DiffuseColor.ToArgb()
        && simulated.EmissionColor.ToArgb() == desired.EmissionColor.ToArgb()
        && Near(simulated.Transparency, desired.Transparency)
        && Near(simulated.IndexOfRefraction, desired.IndexOfRefraction)
        && (simulatedPbr is null) == (desiredPbr is null)
        && (
          simulatedPbr is null
          || desiredPbr is null
          || (Near(simulatedPbr.Metallic, desiredPbr.Metallic) && Near(simulatedPbr.Roughness, desiredPbr.Roughness))
        );
    }
    finally
    {
      simulated.Dispose();
    }
  }

  // Rhino stores these channels as floats and a simulation round-trips them through its own maths, so an exact
  // comparison would report every material as changed.
  private static bool Near(double a, double b) => Math.Abs(a - b) < 1e-6;

  // By-object display colours: HAS_COLOR (geometry → COLOR node) resolved to the owning object's appId → argb, mirroring
  // CreateMaterials. Applied as ObjectColor + ColorSource.ColorFromObject on bake.
  private static Dictionary<string, int> CreateColors(ArtefactBundle bundle, Dictionary<int, int> objByGeom)
  {
    var byObject = new Dictionary<string, int>(StringComparer.Ordinal);
    foreach (var kv in bundle.Relations.ColorByGeometry)
    {
      if (!bundle.Nodes.TryGetValue(kv.Value, out var n) || n.Kind != NodeKind.Color || n.Argb is not int argb)
      {
        continue;
      }
      if (objByGeom.TryGetValue(kv.Key, out int objK) && bundle.ObjectAppIds.TryGetValue(objK, out var appId))
      {
        byObject[appId] = argb;
      }
    }

    // Object-sourced edges (ord=1): a block placement's own colour. A placement owns no geometry, so it never appears
    // in objByGeom — resolve it straight through the object dictionary [ENG-8822, ENG-9114]. Same shape as the Autocad
    // artefact builder's MapColors.
    foreach (var kv in bundle.Relations.ColorByObject)
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
    return byObject;
  }

  // ── helpers ───────────────────────────────────────────────────────────────────────────────────────────
  private static RG.Transform BuildTransform(string? csv, string units, string docUnits)
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
    var t = RG.Transform.Identity;
    t.M00 = d[0];
    t.M01 = d[1];
    t.M02 = d[2];
    t.M03 = d[3] * scale;
    t.M10 = d[4];
    t.M11 = d[5];
    t.M12 = d[6];
    t.M13 = d[7] * scale;
    t.M20 = d[8];
    t.M21 = d[9];
    t.M22 = d[10];
    t.M23 = d[11] * scale;
    t.M30 = d[12];
    t.M31 = d[13];
    t.M32 = d[14];
    t.M33 = d[15];
    return t;
  }

  private static string ObjectUnits(ArtefactBundle bundle, int objK) =>
    bundle.Properties.TryGetValue(objK, out var props)
    && props.TryGetValue("units", out var v)
    && v is string s
    && s.Length > 0
      ? s
      : bundle.Units;

  // The object's source type ("type" scalar, e.g. the Rhino ObjectType or the Revit category), or null when absent.
  private static string? ObjectType(ArtefactBundle bundle, int objK) =>
    bundle.Properties.TryGetValue(objK, out var props)
    && props.TryGetValue("type", out var v)
    && v is string s
    && s.Length > 0
      ? s
      : null;

  // The send side stores "name" as (Attributes.Name || sourceType) alongside the "type" scalar (== sourceType), so an
  // unnamed object has name == type. Returns the real name only when it's present and differs from type; null otherwise
  // (missing, empty, or the sourceType fallback) so unnamed objects stay unnamed on receive.
  private static string? ObjectName(ArtefactBundle bundle, int objK)
  {
    if (!bundle.Properties.TryGetValue(objK, out var props))
    {
      return null;
    }
    if (props.TryGetValue("name", out var nv) && nv is string name && name.Length > 0)
    {
      var type = props.TryGetValue("type", out var tv) && tv is string t ? t : null;
      return string.Equals(name, type, StringComparison.Ordinal) ? null : name;
    }
    return null;
  }

  private void DeepClean(RhinoDoc doc, string baseLayerName)
  {
    try
    {
      // purge this model's groups from the prior receive first — they carry the baseLayerName suffix (BakeGroups)
      for (int i = doc.Groups.Count - 1; i >= 0; i--)
      {
        var group = doc.Groups.FindIndex(i);
        if (group is { Name: not null } && group.Name.Contains(baseLayerName))
        {
          doc.Groups.Delete(i);
        }
      }

      // then this model's generated block definitions — they carry the same baseLayerName suffix (BuildDefinitions).
      // Without this every receive added a fresh "…-(def-K)" set and the block table grew without bound [ENG-9115].
      // Match on the CLEANED suffix: BuildDefinitions runs the whole name through CleanBlockDefinitionName, which
      // rewrites / and \, so a project or model name containing either would not match the raw baseLayerName.
      // deleteReferences: true because the placements are still in the doc at this point (their layers are purged
      // just below); with false, Delete refuses while any reference is alive and the definition would survive.
      var definitionSuffix = RhinoUtils.CleanBlockDefinitionName(baseLayerName);
      for (int i = doc.InstanceDefinitions.Count - 1; i >= 0; i--)
      {
        var definition = doc.InstanceDefinitions[i];
        if (definition is { IsDeleted: false, Name: not null } && definition.Name.Contains(definitionSuffix))
        {
          doc.InstanceDefinitions.Delete(i, true, true);
        }
      }

      int rootLayerIndex = doc.Layers.Find(Guid.Empty, baseLayerName, RhinoMath.UnsetIntIndex);
      if (rootLayerIndex != RhinoMath.UnsetIntIndex)
      {
        var documentLayer = doc.Layers[rootLayerIndex];
        foreach (var layer in documentLayer.GetChildren() ?? Array.Empty<Layer>())
        {
          doc.Layers.Purge(layer.Index, true);
        }
        doc.Layers.Purge(documentLayer.Index, true);
      }
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, "Artefact receive pre-clean failed for '{BaseLayer}'", baseLayerName);
    }
  }

  /// <summary>Minimal <see cref="Base"/> carrier used only as the <c>source</c> of a conversion report entry (UI
  /// highlighting). A plain <see cref="Base"/> (not a custom subclass) so the assembly-scanned TypeLoader accepts it.
  /// The applicationId stands in for the required non-null <c>id</c> (artefact objects aren't deserialized).</summary>
  private static Base Source(string appId) => new() { applicationId = appId, id = appId };
}
