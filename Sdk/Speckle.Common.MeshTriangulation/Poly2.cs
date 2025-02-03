using Speckle.DoubleNumerics;

namespace Speckle.Common.MeshTriangulation;

public readonly struct Poly2
{
  public List<Vector2> Vertices { get; }

  public Poly2()
  {
    Vertices = new List<Vector2>();
  }

  public Poly2(List<Vector2> vertices)
  {
    Vertices = vertices ?? throw new ArgumentNullException(nameof(vertices));
  }
}
