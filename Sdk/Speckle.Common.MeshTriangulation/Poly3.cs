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
}
