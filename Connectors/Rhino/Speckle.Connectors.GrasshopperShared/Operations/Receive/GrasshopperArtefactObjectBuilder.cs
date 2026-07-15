using System.Globalization;
using Rhino;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Converters.Rhino.ToHost.Helpers;
using Speckle.Objects.Other;
using Speckle.Objects.Utils;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Pipelines;
using Speckle.Sdk.Pipelines.Receive.Artifacts;
using RG = Rhino.Geometry;

namespace Speckle.Connectors.GrasshopperShared.Operations.Receive;

/// <summary>
/// Builds the Grasshopper <see cref="SpeckleCollectionWrapper"/> tree directly from a Speckle 4.0 artefact
/// <see cref="ArtefactBundle"/> — the receive-side twin of <c>GrasshopperArtifactRootObjectBuilder</c>. Unlike the
/// Rhino/Revit artefact receivers it does NOT bake into a document; it emits the same wrapper graph the GH Load
/// component outputs onto the canvas (so no <c>IHostObjectBuilder</c>). Geometry is decoded straight from the bundle
/// (meshes/point clouds/curves/points → SGEO via <see cref="SgeoDecoder"/>, raw 3dm →
/// <see cref="RawEncodingToHost.Convert3dm"/>) and reconverted to a clean Speckle <see cref="Base"/> via the shared
/// Rhino converters; the collection tree comes from the bundle's default scene view; per-object properties are
/// carried through.
/// </summary>
/// <remarks>
/// Reconstructs <b>geometry + collections + properties + instances</b>. Instances (DISPLAY_INSTANCE) are resolved
/// through their DEFINITION (DEFINES → geometry) and flattened into transformed geometry wrappers — without this, an
/// instance-only sub-model (e.g. a federated Site/Facades model) receives as empty. Materials (MATERIAL/HAS_MATERIAL)
/// are NOT reconstructed yet — a fast follow. Genuinely non-geometric objects (rooms/levels/areas) are skipped silently;
/// a geometry fragment that fails to decode/convert is reported back via <c>Build</c>'s warnings, not silently dropped.
/// </remarks>
internal sealed class GrasshopperArtefactObjectBuilder
{
  // (root, per-fragment decode/convert warnings) — the caller (ReceiveComponent/ReceiveAsyncComponent) surfaces these
  // as GH runtime messages so an undecodable fragment is visible instead of a silent skip.
  public (SpeckleCollectionWrapper Root, IReadOnlyList<string> Warnings) Build(ArtefactBundle bundle, string rootName)
  {
    var warnings = new List<string>();
    var root = new SpeckleCollectionWrapper
    {
      Base = new Collection { name = rootName },
      Name = rootName,
      Color = null,
      Material = null,
      ApplicationId = Guid.NewGuid().ToString(),
      Path = new List<string> { rootName },
    };

    var collectionCache = new Dictionary<string, SpeckleCollectionWrapper>(StringComparer.Ordinal);
    var rels = bundle.Relations;

    // Instance definitions decoded once (DEFINITION node → its DEFINES + composed DEFINES_INSTANCE geometry), shared
    // across all placements.
    var defGeomByNode = BuildDefinitionGeometry(bundle, rels, warnings);
    // DISPLAY_INSTANCE edges grouped by owning object (an object may place several instances, e.g. a railing → balusters).
    var instEdgesByObject = rels.DisplayInstanceEdges.GroupBy(e => e.Src).ToDictionary(g => g.Key, g => g.ToList());
    // INSTANCE nodes that are a nested/child placement inside some DEFINITION (DEFINES_INSTANCE targets). Their content
    // is already composed into the parent definition's geometry by BuildDefinitionGeometry, so their own top-level
    // DISPLAY_INSTANCE placement (using their un-composed, definition-local transform) must be skipped below — otherwise
    // the nested block appears an extra time at the wrong (definition-space) location instead of once per parent placement.
    var nestedInstanceNodes = new HashSet<int>(rels.DefinesInstanceByDefinition.Values.SelectMany(v => v));

    foreach (var kv in bundle.ObjectAppIds)
    {
      int objK = kv.Key;
      string appId = kv.Value;

      var geometries = DecodeObjectGeometry(objK, bundle, rels, ObjectUnits(bundle, objK), warnings);

      // Instances: an object placed as a block carries no direct SOLID/DISPLAY geometry — its geometry lives on the
      // referenced DEFINITION. Resolve each placement (object → INSTANCE node → DefRef DEFINITION → DEFINES geometry),
      // clone the shared definition geometry and bake in the instance transform so it lands in world space. Without this,
      // entire instance-only sub-models (e.g. a federated Site/Facades model) receive as empty.
      if (instEdgesByObject.TryGetValue(objK, out var instEdges))
      {
        foreach (var e in instEdges)
        {
          if (nestedInstanceNodes.Contains(e.Dst))
          {
            continue; // nested placement — already composed into its parent definition's geometry above
          }
          if (
            !bundle.Nodes.TryGetValue(e.Dst, out var instNode)
            || instNode.DefRef is not int defNodeK
            || !defGeomByNode.TryGetValue(defNodeK, out var defGeoms)
          )
          {
            continue;
          }
          var xf = BuildTransform(instNode.Transform, instNode.Units is { Length: > 0 } u ? u : DocUnits());
          foreach (var g in defGeoms)
          {
            var dup = g.Duplicate();
            dup.Transform(xf);
            geometries.Add(dup);
          }
        }
      }

      if (geometries.Count == 0)
      {
        continue; // non-geometric element (room/level/area) or a definition with no decodable geometry
      }

      bundle.Properties.TryGetValue(objK, out var props);
      var segments = SceneViewResolver.Segments(bundle, objK);
      var collection = GetOrCreateCollection(root, rootName, segments, collectionCache);

      int ord = 0;
      foreach (var rg in geometries)
      {
        Base? converted;
        try
        {
          converted = SpeckleConversionContext.Current.ConvertToSpeckle(rg);
        }
        catch (Exception ex) when (!ex.IsFatal())
        {
          continue; // a fragment the converter can't round-trip — skip without failing the object
        }
        if (converted is null)
        {
          continue;
        }

        converted.applicationId = geometries.Count == 1 ? appId : $"{appId}:g{ord++}";
        var wrapper = new SpeckleGeometryWrapper
        {
          Base = converted,
          GeometryBase = rg,
          Path = collection.Path,
          Parent = collection,
          Color = null,
          Material = null,
        };
        if (props is { Count: > 0 })
        {
          wrapper.Properties = new SpecklePropertyGroupGoo(props);
        }
        collection.Elements.Add(wrapper);
      }
    }

    return (root, warnings);
  }

  // Geometry indices to decode for an object: prefer the lossless SOLID (3dm) blobs, else its DISPLAY meshes.
  private static List<RG.GeometryBase> DecodeObjectGeometry(
    int objK,
    ArtefactBundle bundle,
    ArtefactRelations rels,
    string fallbackUnits,
    List<string> warnings
  )
  {
    var result = new List<RG.GeometryBase>();
    if (rels.SolidByObject.TryGetValue(objK, out var solidKs))
    {
      foreach (var solidK in solidKs)
      {
        result.AddRange(DecodeGeometryIndex(solidK, bundle, fallbackUnits, warnings));
      }
    }
    if (result.Count == 0 && rels.DisplayByObject(objK) is { } displayEdges)
    {
      foreach (var e in displayEdges.OrderBy(x => x.Ord))
      {
        result.AddRange(DecodeGeometryIndex(e.Dst, bundle, fallbackUnits, warnings));
      }
    }
    return result;
  }

  // The send side stores "units" per object as an EAV property, falling back to the bundle's overall units when absent
  // (mirrors RhinoHostObjectArtefactBuilder.ObjectUnits).
  private static string ObjectUnits(ArtefactBundle bundle, int objK) =>
    bundle.Properties.TryGetValue(objK, out var props)
    && props.TryGetValue("units", out var v)
    && v is string s
    && s.Length > 0
      ? s
      : bundle.Units;

  // Decodes every DEFINITION node's geometry once — direct DEFINES → geometry blobs, plus DEFINES_INSTANCE → nested
  // definitions recursively built and composed with their own (definition-local) transform — keyed by definition node
  // index. The same definition is referenced by many instances, so we decode/compose once here and duplicate+transform
  // per placement (mirrors RhinoHostObjectArtefactBuilder.BuildDefinitions' nested composition, ENG-8793): without
  // this, a block-inside-a-block's content is placed once at its own definition-space transform instead of once per
  // parent placement.
  private static Dictionary<int, List<RG.GeometryBase>> BuildDefinitionGeometry(
    ArtefactBundle bundle,
    ArtefactRelations rels,
    List<string> warnings
  )
  {
    var map = new Dictionary<int, List<RG.GeometryBase>>();
    var building = new HashSet<int>();

    List<RG.GeometryBase> BuildDefinition(int defNodeK)
    {
      if (map.TryGetValue(defNodeK, out var already))
      {
        return already; // already built (a shared nested definition is reached from several parents)
      }
      if (!building.Add(defNodeK))
      {
        return new List<RG.GeometryBase>(); // cycle guard — never stack-overflow on a bad bundle
      }

      var geoms = new List<RG.GeometryBase>();

      // direct geometry members (DEFINES → geometry blob).
      if (rels.DefinesByDefinition.TryGetValue(defNodeK, out var geomKs))
      {
        rels.DefinesOrdByDefinition.TryGetValue(defNodeK, out var ords);
        // A member's geometry shares a member ordinal; within each member prefer the authoritative 3dm solid over its
        // display mesh(es) so a solid inside a block rebuilds as a solid (Grasshopper decodes Rhino 3dm, so without
        // this it would place both the solid and its shadow mesh).
        foreach (var memberGeomKs in GroupDefinesByMember(geomKs, ords, bundle))
        {
          foreach (var geomK in memberGeomKs)
          {
            // Definition geometry is in the model's source units; pass the bundle (source) units as the fallback, NOT
            // DocUnits — otherwise a 3dm member isn't rescaled and a block sent from a metre model lands 1000x too
            // small in a millimetre GH doc (mirrors RhinoHostObjectArtefactBuilder.BuildDefinitions).
            geoms.AddRange(DecodeGeometryIndex(geomK, bundle, bundle.Units, warnings));
          }
        }
      }

      // nested block members (DEFINES_INSTANCE → INSTANCE node): build the child definition first (depth-first,
      // memoized above), then duplicate+bake its own transform so a placement of THIS definition already carries the
      // composed nested content.
      if (rels.DefinesInstanceByDefinition.TryGetValue(defNodeK, out var nestedInstNodeKs))
      {
        foreach (var instNodeK in nestedInstNodeKs)
        {
          if (!bundle.Nodes.TryGetValue(instNodeK, out var nestedInst) || nestedInst.DefRef is not int childDefNodeK)
          {
            continue;
          }
          var childGeoms = BuildDefinition(childDefNodeK);
          if (childGeoms.Count == 0)
          {
            continue;
          }
          var xf = BuildTransform(nestedInst.Transform, nestedInst.Units is { Length: > 0 } u ? u : bundle.Units);
          foreach (var g in childGeoms)
          {
            var dup = g.Duplicate();
            dup.Transform(xf);
            geoms.Add(dup);
          }
        }
      }

      building.Remove(defNodeK);
      if (geoms.Count > 0)
      {
        map[defNodeK] = geoms;
      }
      return geoms;
    }

    foreach (var kv in bundle.Nodes)
    {
      if (kv.Value.Kind == NodeKind.Definition)
      {
        BuildDefinition(kv.Key);
      }
    }

    return map;
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
      int ord = ords is not null && i < ords.Count ? ords[i] : -(i + 1);
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

  // Parses an instance node's transform (a 16-value row-major CSV of the 4x4 matrix) into a Rhino transform, scaling
  // the translation from the instance's own units to DocUnits (mirrors RhinoHostObjectArtefactBuilder.BuildTransform;
  // rotation/scale entries are unitless ratios and need no conversion).
  private static RG.Transform BuildTransform(string? csv, string units)
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

    double scale = Units.GetConversionFactor(units, DocUnits());
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

  // Decodes one geometry index to Rhino geometry, scaled from its source units to DocUnits (SGEO carries its own
  // units; 3dm uses the caller-supplied fallback) — mirrors RhinoHostObjectArtefactBuilder.DecodeGeometryIndex.
  // ConvertToSpeckle (below) stamps the *active document's* units onto the converted Base without rescaling the
  // numeric values, so mesh/3dm geometry must already be in DocUnits by the time it gets there.
  private static List<RG.GeometryBase> DecodeGeometryIndex(
    int geomK,
    ArtefactBundle bundle,
    string fallbackUnits,
    List<string> warnings
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
      // Meshes take the fast hand-rolled path (no Base allocation), scaled here.
      if (SgeoDecoder.TryDecodeMesh(g.Content, out var sm))
      {
        var list = new List<RG.GeometryBase> { BuildMesh(sm) };
        ApplyUnits(list, sm.Units);
        return list;
      }
      // Curves, points, and point clouds: decode to a Speckle geometry object and convert via the shared Rhino ToHost
      // converter (ConvertSpeckleGeometry below), which already scales from the decoded Base's own `units` to
      // DocUnits (SpeckleToHostGeometryBaseTopLevelConverter) — so no extra ApplyUnits here, unlike mesh/3dm above.
      // An unsupported/undecodable primitive degrades to nothing (with a warning) rather than aborting the receive.
      Base? decoded = null;
      try
      {
        decoded = SgeoDecoder.Decode(g.Content);
        var converted = ConvertSpeckleGeometry(decoded);
        if (converted.Count == 0)
        {
          warnings.Add($"Geometry {geomK} ({decoded.speckle_type}) did not convert to any native geometry.");
        }
        return converted;
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        string stage = decoded is null ? "decode" : $"convert of {decoded.speckle_type}";
        warnings.Add($"Geometry {geomK} ({g.Type}) failed to {stage}: {ex.Message}");
      }
    }
    return new List<RG.GeometryBase>();
  }

  // Speckle geometry object (from SgeoDecoder.Decode) → Rhino geometry via the shared Rhino ToHost converter (the
  // same one Rhino's own artefact receive uses, ENG-8781). Mirrors RhinoHostObjectArtefactBuilder.ConvertSpeckleGeometry.
  private static List<RG.GeometryBase> ConvertSpeckleGeometry(Base decoded) =>
    SpeckleConversionContext
      .Current.ConvertToHost(decoded)
      .Select(pair => pair.Item1)
      .OfType<RG.GeometryBase>()
      .ToList();

  private static void ApplyUnits(List<RG.GeometryBase> geoms, string? units)
  {
    if (units is not { Length: > 0 } u)
    {
      return;
    }
    var docUnits = DocUnits();
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

  // The active Rhino/GH document's units — what ConvertToSpeckle's converters (e.g. MeshToSpeckleConverter) stamp
  // as the converted Base's "units" without rescaling, so all decoded geometry must land here before conversion.
  private static string DocUnits() => RhinoDoc.ActiveDoc?.ModelUnitSystem.ToSpeckleString() ?? Units.Meters;

  // SGEO neutral mesh → Rhino mesh (Speckle count-prefixed face format; mirrors RhinoHostObjectArtefactBuilder).
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
        for (int k = 1; k < n - 1; k++)
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
        mesh.VertexColors.Add(System.Drawing.Color.FromArgb(argb));
      }
    }
    mesh.Normals.ComputeNormals();
    mesh.Compact();
    return mesh;
  }

  // Resolves (and creates once) the nested collection-wrapper chain for the given scene-view segments under the root.
  private static SpeckleCollectionWrapper GetOrCreateCollection(
    SpeckleCollectionWrapper root,
    string rootName,
    IReadOnlyList<string> segments,
    Dictionary<string, SpeckleCollectionWrapper> cache
  )
  {
    var previous = root;
    var current = new List<string> { rootName };
    foreach (var raw in segments)
    {
      var name = string.IsNullOrWhiteSpace(raw) ? "unnamed" : raw;
      current.Add(name);
      var key = string.Concat(current);
      if (cache.TryGetValue(key, out var existing))
      {
        previous = existing;
        continue;
      }

      var wrapper = new SpeckleCollectionWrapper
      {
        Base = new Collection { name = name },
        Name = name,
        ApplicationId = key,
        Path = new List<string>(current),
        Color = null,
        Material = null,
      };
      cache[key] = wrapper;
      previous.Elements.Add(wrapper);
      previous = wrapper;
    }
    return previous;
  }
}
