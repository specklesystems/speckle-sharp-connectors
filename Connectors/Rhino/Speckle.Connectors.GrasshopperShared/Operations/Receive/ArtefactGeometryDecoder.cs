using Rhino;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Converters.Rhino.ToHost.Helpers;
using Speckle.Objects.Other;
using Speckle.Objects.Utils;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models;
using Speckle.Sdk.Pipelines.Receive.Artifacts;
using RG = Rhino.Geometry;
using SOG = Speckle.Objects.Geometry;

namespace Speckle.Connectors.GrasshopperShared.Operations.Receive;

/// <summary>
/// Turns one geometry index of an artefact bundle into Rhino geometry, in the active document's units.
/// </summary>
/// <remarks>
/// Shared with <see cref="GrasshopperArtefactObjectBuilder"/> so Explore decodes the geometry receive does NOT
/// bake — a CENTERLINE curve — through the same path, not a second half-equivalent copy of it.
/// </remarks>
internal static class ArtefactGeometryDecoder
{
  // Decodes one geometry index to Rhino geometry, scaled from its source units to DocUnits (SGEO carries its own
  // units; 3dm uses the caller-supplied fallback) — mirrors RhinoHostObjectArtefactBuilder.DecodeGeometryIndex.
  // ConvertToSpeckle (below) stamps the *active document's* units onto the converted Base without rescaling the
  // numeric values, so mesh/3dm geometry must already be in DocUnits by the time it gets there.
  internal static List<RG.GeometryBase> DecodeGeometryIndex(
    int geomK,
    ArtefactBundle bundle,
    string fallbackUnits,
    string? sourceType,
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
        decoded = AsSourceType(SgeoDecoder.Decode(g.Content), sourceType);
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
  internal static string DocUnits() => RhinoDoc.ActiveDoc?.ModelUnitSystem.ToSpeckleString() ?? Units.Meters;

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

  // SGEO encodes a single point and a whole point cloud under the same Points primitive, so Decode can only ever hand
  // back a Pointcloud - and a Speckle Point came back as a one-point cloud, a different type to everything downstream
  // [ENG-9162]. The object's source type is the discriminator the blob lacks. Mirrors RhinoHostObjectArtefactBuilder,
  // which keys on Rhino's own ObjectType; here the send stamps the Speckle type instead.
  private static Base AsSourceType(Base decoded, string? sourceType) =>
    sourceType is not null
    && sourceType.EndsWith(".Point", StringComparison.Ordinal)
    && decoded is SOG.Pointcloud { points.Count: 3 } cloud
      ? new SOG.Point(cloud.points[0], cloud.points[1], cloud.points[2], cloud.units)
      : decoded;
}
