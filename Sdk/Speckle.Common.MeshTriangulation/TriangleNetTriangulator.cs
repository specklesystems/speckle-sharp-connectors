using Speckle.DoubleNumerics;
using TriangleNet.Geometry;

namespace Speckle.Common.MeshTriangulation;

public class TriangleNetTriangulator : ITriangulator
{
  public Mesh2 Triangulate(IReadOnlyList<Poly2> polygons)
  {
    var poly = new Polygon();

    AddContour(poly, polygons[0]);

    for (int i = 1; i < polygons.Count; i++)
    {
      AddHole(poly, polygons[i]);
    }

    var mesh = poly.Triangulate();
    var vertices = new List<Vector2>();
    var triangles = new List<int>();
    int tc = 0;

    foreach (var triangle in mesh.Triangles)
    {
      var v1 = triangle.GetVertex(0);
      var v2 = triangle.GetVertex(1);
      var v3 = triangle.GetVertex(2);

      vertices.Add(new Vector2(v1.X, v1.Y));
      vertices.Add(new Vector2(v2.X, v2.Y));
      vertices.Add(new Vector2(v3.X, v3.Y));

      triangles.Add(tc++);
      triangles.Add(tc++);
      triangles.Add(tc++);
    }

    return new Mesh2(vertices, triangles);
  }

  private void AddContour(Polygon p, Poly2 poly2)
  {
    var verts = new List<Vertex>();
    foreach (var v in poly2.Vertices)
    {
      verts.Add(new Vertex(v.X, v.Y, 1));
    }

    p.Add(new Contour(verts, 1));
  }

  private void AddHole(Polygon p, Poly2 poly2)
  {
    var verts = new List<Vertex>();
    foreach (var v in poly2.Vertices)
    {
      verts.Add(new Vertex(v.X, v.Y, 2));
    }

    var left = GetLeftPoint(poly2.Vertices[0], poly2.Vertices[1], 0.01f);
    p.Add(new Contour(verts, 2), new Point(left.X, left.Y));
  }

  private Vector2 GetLeftPoint(Vector2 a, Vector2 b, double distance)
  {
    Vector2 ab = b - a;
    Vector2 leftVector = new Vector2(-ab.Y, ab.X);
    var left = Vector2.Normalize(leftVector);
    left *= distance;
    var m = Vector2.Lerp(a, b, 0.5f);
    return m + left;
  }
}
