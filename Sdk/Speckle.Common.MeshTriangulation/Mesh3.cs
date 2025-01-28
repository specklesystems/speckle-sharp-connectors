using Speckle.DoubleNumerics;

namespace Speckle.Common.MeshTriangulation;

public readonly struct Mesh3
{
  public List<Vector3> Vertices { get; }
  public List<int> Triangles { get; }

  public Mesh3(List<Vector3> vertices, List<int> triangles)
  {
    Vertices = vertices ?? throw new ArgumentNullException(nameof(vertices));
    Triangles = triangles ?? throw new ArgumentNullException(nameof(triangles));

    if (triangles.Count % 3 != 0)
    {
      throw new ArgumentException("Triangles list must be divisible by 3, as each triangle requires 3 indices.");
    }
  }
}
