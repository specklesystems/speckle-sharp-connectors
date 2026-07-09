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
    var bakedObjectIds = new HashSet<string>();
    var conversionResults = new HashSet<ReceiveConversionResult>();

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
    using (session.Phase("Materials"))
    {
      (materialByObject, materialByGeometry) = CreateMaterials(doc, bundle, objByGeom);
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
          var ids = new List<Guid>();
          foreach (var geom in geometries)
          {
            if (geom is RG.Hatch hatch)
            {
              // restore pattern/rotation/scale carried as EAV onto the Hatch rebuilt from the SGEO Region
              bundle.Properties.TryGetValue(objK, out var hatchProps);
              RhinoHatchStyler.Apply(doc, hatch, hatchProps, _converterSettings.Current.SpeckleUnits);
            }
            ids.Add(BakeObject(doc, geom, layerIndex, materialByObject, colorByObject, appId, name));
          }
          bakedObjectIds.UnionWith(ids.Select(g => g.ToString()));
          conversionResults.Add(new(Status.SUCCESS, source, ids[0].ToString(), "Speckle.Object"));
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
          layerCache,
          materialByObject,
          materialByGeometry,
          bakedObjectIds,
          conversionResults,
          session
        );
      }
    }

    doc.Views.Redraw();
    return new HostObjectBuilderResult(bakedObjectIds, conversionResults);
  }

  // ── geometry ──────────────────────────────────────────────────────────────────────────────────────────
  private List<RG.GeometryBase> DecodeObjectGeometry(
    int objK,
    ArtefactBundle bundle,
    ArtefactRelations rels,
    string fallbackUnits
  )
  {
    _lastDecodeFailure = null;
    var result = new List<RG.GeometryBase>();
    if (rels.SolidByObject.TryGetValue(objK, out var solidKs))
    {
      foreach (var solidK in solidKs)
      {
        result.AddRange(DecodeGeometryIndex(solidK, bundle, fallbackUnits));
      }
    }
    if (result.Count == 0 && rels.DisplayByObject(objK) is { } displayEdges)
    {
      foreach (var e in displayEdges.OrderBy(x => x.Ord))
      {
        result.AddRange(DecodeGeometryIndex(e.Dst, bundle, fallbackUnits));
      }
    }
    return result;
  }

  // Decodes one geometry index to Rhino geometry, scaled to doc units (SGEO carries its own units; 3dm uses fallback).
  private List<RG.GeometryBase> DecodeGeometryIndex(int geomK, ArtefactBundle bundle, string fallbackUnits)
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
      // Meshes take the fast hand-rolled path (no Base allocation), scaled here.
      if (SgeoDecoder.TryDecodeMesh(g.Content, out var sm))
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
        decoded = SgeoDecoder.Decode(g.Content);
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
        for (int k = 1; k < n - 1; k++) // fan-triangulate n-gons
        {
          mesh.Faces.AddFace(f[p + 1], f[p + 1 + k], f[p + 2 + k]);
        }
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
    mesh.Compact();
    return mesh;
  }

  private Guid BakeObject(
    RhinoDoc doc,
    RG.GeometryBase geom,
    int layerIndex,
    Dictionary<string, Guid> materialByObject,
    Dictionary<string, int> colorByObject,
    string appId,
    string? name
  )
  {
    var atts = new ObjectAttributes { LayerIndex = layerIndex };
    if (name is { Length: > 0 })
    {
      atts.Name = name;
    }
    if (materialByObject.TryGetValue(appId, out Guid materialGuid))
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

  // ── instances ─────────────────────────────────────────────────────────────────────────────────────────
#pragma warning disable CA1506
  private void BakeInstances(
#pragma warning restore CA1506
    RhinoDoc doc,
    ArtefactBundle bundle,
    ArtefactRelations rels,
    int baseLayerIndex,
    Dictionary<string, int> layerCache,
    Dictionary<string, Guid> materialByObject,
    Dictionary<int, Guid> materialByGeometry,
    HashSet<string> bakedObjectIds,
    HashSet<ReceiveConversionResult> conversionResults,
    ArtefactSessionLog session
  )
  {
    var docUnits = _converterSettings.Current.SpeckleUnits;

    // definitions (incl. nested blocks): a DEFINITION node owns its geometry directly and may contain nested placements.
    var defIndexByNode = BuildDefinitions(doc, bundle, rels, materialByGeometry, docUnits, session);

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
      if (materialByObject.TryGetValue(appId, out Guid materialGuid))
      {
        atts.RenderMaterial = RenderContent.FromId(doc, materialGuid) as RhinoRenderMaterial;
        atts.MaterialSource = ObjectMaterialSource.MaterialFromObject;
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

  // Builds every DEFINITION node into a Rhino InstanceDefinition, returning node K → Rhino defIndex. A DEFINITION owns
  // its geometry directly (DEFINES → geometry blobs) and may also contain nested block placements (DEFINES_INSTANCE →
  // INSTANCE node). Nested definitions are built depth-first (memoized in defIndexByNode) so a parent can reference the
  // child definition's Rhino Guid via an InstanceReferenceGeometry member carrying the nested placement's own transform.
  // The geometry's material (HAS_MATERIAL → geometry) is baked onto the members so placed instances aren't all grey.
  private Dictionary<int, int> BuildDefinitions(
    RhinoDoc doc,
    ArtefactBundle bundle,
    ArtefactRelations rels,
    Dictionary<int, Guid> materialByGeometry,
    string docUnits,
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
            var decoded = DecodeGeometryIndex(geomK, bundle, bundle.Units);
            materialByGeometry.TryGetValue(geomK, out Guid mg);
            foreach (var geom in decoded)
            {
              geometryList.Add(geom);
              // ObjectAttributes is IDisposable but InstanceDefinitions.Add takes ownership — disposing here would
              // corrupt the definition; the doc owns them for the document lifetime.
#pragma warning disable CA2000
              var a = new ObjectAttributes();
#pragma warning restore CA2000
              if (mg != Guid.Empty)
              {
                a.RenderMaterial = RenderContent.FromId(doc, mg) as RhinoRenderMaterial;
                a.MaterialSource = ObjectMaterialSource.MaterialFromObject;
              }
              attributeList.Add(a);
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
#pragma warning disable CA2000
          var nestedAtts = new ObjectAttributes();
#pragma warning restore CA2000
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
      var defName = RhinoUtils.CleanBlockDefinitionName($"{defNode.Name ?? "Definition"}-(def-{defNodeK})");
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
  private (Dictionary<string, Guid> byObject, Dictionary<int, Guid> byGeometry) CreateMaterials(
    RhinoDoc doc,
    ArtefactBundle bundle,
    Dictionary<int, int> objByGeom
  )
  {
    var guidByMaterialNode = new Dictionary<int, Guid>();
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
        var renderMaterial = RhinoRenderMaterial.CreateBasicMaterial(rhinoMaterial, doc);
        doc.RenderMaterials.Add(renderMaterial);
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
    return (byObject, byGeometry);
  }

  // By-object display colours: HAS_COLOR (geometry → COLOR node) resolved to the owning object's appId → argb, mirroring
  // CreateMaterials. Applied as ObjectColor + ColorSource.ColorFromObject on bake.
  private static Dictionary<string, int> CreateColors(ArtefactBundle bundle, Dictionary<int, int> objByGeom)
  {
    var byObject = new Dictionary<string, int>();
    foreach (var kv in bundle.Relations.ColorByGeometry)
    {
      if (
        !bundle.Nodes.TryGetValue(kv.Value, out var n)
        || n.Kind != NodeKind.Color
        || n.Argb is not int argb
      )
      {
        continue;
      }
      if (objByGeom.TryGetValue(kv.Key, out int objK) && bundle.ObjectAppIds.TryGetValue(objK, out var appId))
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
