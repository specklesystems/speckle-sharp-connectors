using Speckle.Converter.MicroStation.Settings;
using Speckle.Converters.Common;
using Speckle.Objects.Geometry;
using Speckle.Sdk.Models;

namespace Speckle.Converter.MicroStation.ToSpeckle.TopLevel;

/// <summary>
/// Converts a MicroStation 2026 COM <see cref="MSIDGN.SmartSolidElement"/> (parametric 3D solid) to a
/// Speckle <see cref="Mesh"/> by tessellating it via <c>FacetSolidAsShapes</c>.
/// Each returned <see cref="MSIDGN.ShapeElement"/> is a planar polygon face of the tessellated solid.
/// </summary>
[NameAndRankValue(typeof(MSIDGN.SmartSolidElement), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class SolidElementConverter(IConverterSettingsStore<MicroStationConversionSettings> settingsStore)
  : IToSpeckleTopLevelConverter
{
  public Base Convert(object target) => Convert((MSIDGN.SmartSolidElement)target);

  private Mesh Convert(MSIDGN.SmartSolidElement element)
  {
    var s = settingsStore.Current;

    // FacetSolidAsShapes: maxEdges=0 (auto), maxEdgeLength=0 (auto),
    // chordTolerance=0.001 (master units), normalTolerance=0.1 (radians)
    var shapes = element.FacetSolidAsShapes(0, 0, 0.001, 0.1);

    var vertices = new List<double>();
    var faces = new List<int>();

    while (shapes.MoveNext())
    {
      var shape = shapes.Current?.AsShapeElement();
      if (shape == null)
      {
        continue;
      }

      var pts = shape.GetVertices();
      int startIndex = vertices.Count / 3;

      foreach (var pt in pts)
      {
        vertices.Add(pt.X);
        vertices.Add(pt.Y);
        vertices.Add(pt.Z);
      }

      // Encode as n-gon: [n, i0, i1, ..., in-1]
      faces.Add(pts.Length);
      for (int i = 0; i < pts.Length; i++)
      {
        faces.Add(startIndex + i);
      }
    }

    if (vertices.Count == 0)
    {
      return CreateBoundingBoxMesh(element, s.SpeckleUnits);
    }

    return new Mesh
    {
      vertices = vertices,
      faces = faces,
      units = s.SpeckleUnits,
      applicationId = element.ID.ToString(),
    };
  }

  private static Mesh CreateBoundingBoxMesh(MSIDGN.SmartSolidElement element, string units)
  {
    var range = element.Range;
    var lo = range.Low;
    var hi = range.High;

    var verts = new List<double>
    {
      lo.X, lo.Y, lo.Z,
      hi.X, lo.Y, lo.Z,
      hi.X, hi.Y, lo.Z,
      lo.X, hi.Y, lo.Z,
      lo.X, lo.Y, hi.Z,
      hi.X, lo.Y, hi.Z,
      hi.X, hi.Y, hi.Z,
      lo.X, hi.Y, hi.Z,
    };

    var faces = new List<int> { 4, 0, 1, 2, 3, 4, 4, 5, 6, 7, 4, 0, 1, 5, 4, 4, 2, 3, 7, 6, 4, 1, 2, 6, 5, 4, 0, 3, 7, 4 };

    return new Mesh
    {
      vertices = verts,
      faces = faces,
      units = units,
      applicationId = element.ID.ToString(),
    };
  }
}
