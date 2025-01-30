namespace Speckle.Common.MeshTriangulation;

public interface ITriangulator
{
  Mesh2 Triangulate(IReadOnlyList<Poly2> polygons);
}
