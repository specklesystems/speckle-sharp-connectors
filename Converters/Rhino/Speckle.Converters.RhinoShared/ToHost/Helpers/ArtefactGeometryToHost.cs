using Speckle.Objects.Utils;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Rhino.ToHost.Helpers;

/// <summary>Why a geometry blob came back empty. <see cref="Exception"/> is null when it converted to nothing.</summary>
public readonly record struct ArtefactGeometryFailure(string Stage, Exception? Exception);

/// <summary>
/// One artefact geometry blob (SGEO or raw 3dm) to Rhino geometry in doc units. Shared by the Rhino and Grasshopper
/// artefact receive.
/// </summary>
public static class ArtefactGeometryToHost
{
  private const string RHINO_POINT_TYPE = "Point";

  public static List<RG.GeometryBase> Decode(
    byte[] content,
    string type,
    string fallbackUnits,
    string docUnits,
    string? sourceType,
    Func<Base, IEnumerable<RG.GeometryBase>> convert,
    out ArtefactGeometryFailure? failure
  )
  {
    failure = null;
    if (type == SO.RawEncodingFormats.RHINO_3DM)
    {
      var geoms = RawEncodingToHost.Convert3dm(content);
      ApplyUnits(geoms, fallbackUnits, docUnits);
      return geoms;
    }
    if (!IsSgeo(content))
    {
      return new List<RG.GeometryBase>();
    }

    Base? decoded = null;
    try
    {
      // SgeoMesh carries no normals or UVs, so a mesh with either takes the full decoder [ENG-9214]
      if (IsFastPathMesh(SgeoDecoder.ReadHeader(content)) && SgeoDecoder.TryDecodeMesh(content, out var sm))
      {
        var list = new List<RG.GeometryBase> { BuildMesh(sm) };
        ApplyUnits(list, sm.Units, docUnits);
        return list;
      }

      // the converter scales to doc units itself
      decoded = AsSourceType(SgeoDecoder.Decode(content), sourceType);
      var converted = convert(decoded).ToList();
      if (converted.Count == 0)
      {
        failure = new($"convert of {decoded.speckle_type}", null);
      }
      return converted;
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      failure = new(decoded is null ? "decode" : $"convert of {decoded.speckle_type}", ex);
      return new List<RG.GeometryBase>();
    }
  }

  // Speckle count-prefixed faces; n-gons are fan-triangulated and kept as MeshNgon records, as in MeshToHostConverter
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
        mesh.VertexColors.Add(System.Drawing.Color.FromArgb(argb));
      }
    }
    mesh.Normals.ComputeNormals();
    // no unreferenced vertices to cull, and Compact would reindex the fresh n-gon records
    if (mesh.Ngons.Count == 0)
    {
      mesh.Compact();
    }
    return mesh;
  }

  private static bool IsSgeo(byte[] content) =>
    content.Length >= 4 && content[0] == 'S' && content[1] == 'G' && content[2] == 'E' && content[3] == 'O';

  private static bool IsFastPathMesh(SgeoHeader header) =>
    header.PrimitiveType == SgeoPrimitiveType.Mesh && (header.Flags & (SgeoFlags.HasNormals | SgeoFlags.HasUvs)) == 0;

  // SGEO has one Points primitive for both, so the object's source type tells a point from a one-point cloud
  // [ENG-9162, ENG-9215]. Rhino stamps its ObjectType ("Point"), Grasshopper the speckle_type.
  private static Base AsSourceType(Base decoded, string? sourceType) =>
    IsPointType(sourceType) && decoded is SOG.Pointcloud { points.Count: 3 } cloud
      ? new SOG.Point(cloud.points[0], cloud.points[1], cloud.points[2], cloud.units)
      : decoded;

  private static bool IsPointType(string? sourceType) =>
    sourceType is not null
    && (
      string.Equals(sourceType, RHINO_POINT_TYPE, StringComparison.Ordinal)
      || sourceType.EndsWith(".Point", StringComparison.Ordinal)
    );

  private static void ApplyUnits(List<RG.GeometryBase> geoms, string? units, string docUnits)
  {
    if (units is not { Length: > 0 } u || string.Equals(u, docUnits, StringComparison.OrdinalIgnoreCase))
    {
      return;
    }
    var t = RG.Transform.Scale(RG.Point3d.Origin, Units.GetConversionFactor(u, docUnits));
    foreach (var geom in geoms)
    {
      geom.Transform(t);
    }
  }
}
