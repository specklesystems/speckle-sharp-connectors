using Speckle.Converter.MicroStation.Settings;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Registration;
using Speckle.Objects.Geometry;
using Speckle.Sdk.Models;

namespace Speckle.Converter.MicroStation.ToSpeckle.TopLevel;

/// <summary>
/// Converts a MicroStation 2026 COM <see cref="MSIDGN.MeshElement"/> (polygon mesh geometry) to a
/// Speckle <see cref="Mesh"/>. The COM API returns vertices in master units (no UOR scaling needed).
/// Mesh elements in DGN files store triangulated facet data; faces are encoded as triangles.
/// </summary>
[NameAndRankValue(typeof(MSIDGN.MeshElement), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class MeshElementConverter(IConverterSettingsStore<MicroStationConversionSettings> settingsStore)
  : IToSpeckleTopLevelConverter
{
  public Base Convert(object target) => Convert((MSIDGN.MeshElement)target);

  private Mesh Convert(MSIDGN.MeshElement element)
  {
    var s = settingsStore.Current;

    // GetVertices() returns Point3d[] in master units
    var pts = element.GetVertices();
    var vertices = new List<double>(pts.Length * 3);
    foreach (var pt in pts)
    {
      vertices.Add(pt.X);
      vertices.Add(pt.Y);
      vertices.Add(pt.Z);
    }

    // GetFacetIndices() returns a flat int[] where each consecutive triple is one triangular face
    var indices = element.GetFacetIndices();
    var faces = new List<int>(indices.Length / 3 * 4);
    for (int i = 0; i + 2 < indices.Length; i += 3)
    {
      faces.Add(3);
      faces.Add(indices[i]);
      faces.Add(indices[i + 1]);
      faces.Add(indices[i + 2]);
    }

    return new Mesh
    {
      vertices = vertices,
      faces = faces,
      units = s.SpeckleUnits,
      applicationId = element.ID.ToString(),
    };
  }
}
