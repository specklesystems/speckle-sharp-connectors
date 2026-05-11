using Bentley.GeometryNET;
using Speckle.Converter.MicroStation.Settings;
using Speckle.Converters.Common;
using Speckle.Objects.Geometry;

namespace Speckle.Converter.MicroStation.ToSpeckle.TopLevel;

/// <summary>
/// Converts a Bentley.DgnPlatformNET <see cref="MgdMeshHeader"/> directly into a Speckle
/// <see cref="Mesh"/> by reading its stored <see cref="PolyfaceHeader"/>.
/// <para>
/// This is the first managed-API converter in the connector — takes the managed element as-is
/// from the dispatcher (no COM bridge needed since the element flows through the pipeline as
/// <see cref="MgdElement"/>). MicroStation's COM interop has no typed wrapper for mesh elements
/// at all, so the COM-based converters can't handle them; the managed surface is the only path.
/// </para>
/// <para>
/// <see cref="MgdMeshHeader.GetMeshData"/> is a pure data-read on already-tessellated mesh
/// storage — it doesn't touch the rendering engine (BrowseTriangleMesh / ElementGraphicsOutput),
/// which is what triggered process-terminating CSEs in earlier attempts at managed-API
/// integration. If a CSE surfaces here despite that, the per-element try/catch in the root
/// dispatcher can't catch it; managed exceptions fall through to the bounding-box fallback.
/// </para>
/// </summary>
public class MeshHeaderElementConverter(IConverterSettingsStore<MicroStationConversionSettings> settingsStore)
{
  public Mesh Convert(MgdMeshHeader mgdMesh)
  {
    var idValue = (ulong)mgdMesh.ElementId;
    var polyface =
      mgdMesh.GetMeshData()
      ?? throw new InvalidOperationException($"MeshHeaderElement {idValue} returned a null PolyfaceHeader.");

    return Convert(polyface, idValue.ToString());
  }

  private Mesh Convert(PolyfaceHeader polyface, string applicationId)
  {
    var s = settingsStore.Current;

    // Vertex list: Point is IEnumerable<DPoint3d> in master units.
    var pointList = polyface.Point.ToArray();
    var vertices = new List<double>(pointList.Length * 3);
    foreach (var pt in pointList)
    {
      vertices.Add(pt.X);
      vertices.Add(pt.Y);
      vertices.Add(pt.Z);
    }

    // Bentley PolyfaceHeader index encoding (variable-size facets):
    //   - indices are 1-based into the Point array
    //   - sign is edge-visibility (negative = hidden edge); we ignore visibility, take Abs
    //   - 0 terminates the current facet
    // Speckle's Mesh.faces uses the n-gon encoding [n, i0, i1, ..., in-1] with 0-based vertex
    // indices and no explicit terminator. Translate accordingly.
    var indices = polyface.PointIndex.ToArray();
    var faces = new List<int>(indices.Length);
    int i = 0;
    while (i < indices.Length)
    {
      int facetStart = i;
      while (i < indices.Length && indices[i] != 0)
      {
        i++;
      }
      int facetSize = i - facetStart;
      if (facetSize >= 3)
      {
        faces.Add(facetSize);
        for (int j = facetStart; j < i; j++)
        {
          faces.Add(Math.Abs(indices[j]) - 1);
        }
      }
      i++; // skip the 0 terminator
    }

    return new Mesh
    {
      vertices = vertices,
      faces = faces,
      units = s.SpeckleUnits,
      applicationId = applicationId,
    };
  }
}
