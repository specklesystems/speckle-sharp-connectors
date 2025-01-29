using Speckle.DoubleNumerics;

namespace Speckle.Common.MeshTriangulation;

public readonly struct Poly3
{
  public List<Vector3> Vertices { get; }

  public Poly3()
  {
    Vertices = new List<Vector3>();
  }

  public Poly3(List<Vector3> vertices)
  {
    Vertices = vertices ?? throw new ArgumentNullException(nameof(vertices));
  }

  // helper function, only works on polygons with at least 3 vertices
  public Vector3 GetNormal()
  {
    if (Vertices.Count < 3)
    {
      throw new InvalidOperationException("Polygon must have at least 3 points to calculate a normal.");
    }

    var v1 = Vertices[1] - Vertices[0];
    var v2 = Vertices[2] - Vertices[0];
    return Vector3.Normalize(Vector3.Cross(v1, v2));
  }

  public void Reverse()
  {
    Vertices.Reverse();
  }
}
