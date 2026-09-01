using Speckle.Converters.MicroStation.Services;

namespace Speckle.Converters.MicroStation.ToSpeckle.Raw;

/// <summary>
/// Bentley <see cref="BG.PolyfaceHeader"/> → Speckle <see cref="SOG.Mesh"/>. Handles both polyface
/// index encodings:
/// <list type="bullet">
/// <item><b>0-terminated variable facets</b> (NumberPerFace &lt;= 1): 1-based indices, sign carries
/// edge visibility (ignored — take Abs), 0 terminates the facet.</item>
/// <item><b>Fixed-block facets</b> (NumberPerFace &gt; 1): blocks of exactly NumberPerFace indices,
/// 0 entries pad short facets.</item>
/// </list>
/// Vertices flow through <see cref="GeometryMapper"/> (ambient transform + global origin + UOR scale).
/// Output faces use Speckle's n-gon encoding [n, i0, …, i(n-1)] with 0-based indices.
/// </summary>
public class PolyfaceConverter(GeometryMapper mapper)
{
  public SOG.Mesh? Convert(BG.PolyfaceHeader polyface)
  {
    var vertices = new List<double>();
    foreach (BG.DPoint3d p in polyface.Point)
    {
      var (x, y, z) = mapper.MapXyz(p);
      vertices.Add(x);
      vertices.Add(y);
      vertices.Add(z);
    }
    int vertexCount = vertices.Count / 3;
    if (vertexCount == 0)
    {
      return null;
    }

    int[] indices = [.. polyface.PointIndex];
    var faces = new List<int>(indices.Length + indices.Length / 3);
    uint numberPerFace = polyface.NumberPerFace;

    if (numberPerFace > 1)
    {
      for (int blockStart = 0; blockStart + (int)numberPerFace <= indices.Length; blockStart += (int)numberPerFace)
      {
        AppendFacet(faces, indices, blockStart, blockStart + (int)numberPerFace, vertexCount);
      }
    }
    else
    {
      int i = 0;
      while (i < indices.Length)
      {
        int facetStart = i;
        while (i < indices.Length && indices[i] != 0)
        {
          i++;
        }
        AppendFacet(faces, indices, facetStart, i, vertexCount);
        i++; // skip the 0 terminator
      }
    }

    if (faces.Count == 0)
    {
      return null;
    }

    return new SOG.Mesh
    {
      vertices = vertices,
      faces = faces,
      units = mapper.Units,
    };
  }

  private static void AppendFacet(List<int> faces, int[] indices, int start, int end, int vertexCount)
  {
    Span<int> facet = stackalloc int[Math.Min(end - start, 64)];
    int n = 0;
    for (int j = start; j < end && n < facet.Length; j++)
    {
      int raw = Math.Abs(indices[j]);
      if (raw == 0)
      {
        break; // fixed-block padding
      }
      int zeroBased = raw - 1;
      if (zeroBased < 0 || zeroBased >= vertexCount)
      {
        return; // out-of-range facet — drop whole facet (matches dgnextract's index guard)
      }
      facet[n++] = zeroBased;
    }
    if (n < 3)
    {
      return;
    }
    faces.Add(n);
    for (int j = 0; j < n; j++)
    {
      faces.Add(facet[j]);
    }
  }
}
