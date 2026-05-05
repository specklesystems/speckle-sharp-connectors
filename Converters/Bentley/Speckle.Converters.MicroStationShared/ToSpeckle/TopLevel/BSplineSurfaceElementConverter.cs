using Speckle.Converter.MicroStation.Settings;
using Speckle.Converters.Common;
using Speckle.Objects.Geometry;
using Speckle.Sdk.Models;

namespace Speckle.Converter.MicroStation.ToSpeckle.TopLevel;

/// <summary>
/// Converts a MicroStation 2026 COM <see cref="MSIDGN.BsplineSurfaceElement"/> to a Speckle
/// <see cref="Mesh"/> by building a quad mesh from the B-spline control pole grid.
/// <c>BsplineSurface.GetPoles()</c> returns a 2-D array indexed [vIndex, uIndex].
/// </summary>
[NameAndRankValue(typeof(MSIDGN.BsplineSurfaceElement), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class BSplineSurfaceElementConverter(IConverterSettingsStore<MicroStationConversionSettings> settingsStore)
  : IToSpeckleTopLevelConverter
{
  public Base Convert(object target) => Convert((MSIDGN.BsplineSurfaceElement)target);

  private Mesh Convert(MSIDGN.BsplineSurfaceElement element)
  {
    var s = settingsStore.Current;

    var surface = element.ExtractBsplineSurface();
    var poles = surface.GetPoles(); // Point3d[vCount, uCount]

    int vCount = surface.VPolesCount;
    int uCount = surface.UPolesCount;

    if (uCount < 2 || vCount < 2)
    {
      return CreateBoundingBoxMesh(element, s.SpeckleUnits);
    }

    var vertices = new List<double>(uCount * vCount * 3);
    for (int v = 0; v < vCount; v++)
    {
      for (int u = 0; u < uCount; u++)
      {
        var pt = poles[v, u];
        vertices.Add(pt.X);
        vertices.Add(pt.Y);
        vertices.Add(pt.Z);
      }
    }

    // Build quad faces from adjacent poles: (v,u), (v,u+1), (v+1,u+1), (v+1,u)
    var faces = new List<int>((uCount - 1) * (vCount - 1) * 5);
    for (int v = 0; v < vCount - 1; v++)
    {
      for (int u = 0; u < uCount - 1; u++)
      {
        faces.Add(4);
        faces.Add(v * uCount + u);
        faces.Add(v * uCount + u + 1);
        faces.Add((v + 1) * uCount + u + 1);
        faces.Add((v + 1) * uCount + u);
      }
    }

    return new Mesh
    {
      vertices = vertices,
      faces = faces,
      units = s.SpeckleUnits,
      applicationId = element.ID.ToString(),
    };
  }

  private static Mesh CreateBoundingBoxMesh(MSIDGN.BsplineSurfaceElement element, string units)
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
