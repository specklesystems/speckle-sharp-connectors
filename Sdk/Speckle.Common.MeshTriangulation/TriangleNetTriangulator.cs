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

    p.Add(new TriangleNet.Geometry.Contour(verts, 1));
  }

  private void AddHole(Polygon p, Poly2 poly2)
  {
    var verts = new List<Vertex>();
    foreach (var v in poly2.Vertices)
    {
      verts.Add(new Vertex(v.X, v.Y, 2));
    }

    // we need a point inside the hole to add it
    var pointInHole = GetRightPointOfEdge(poly2.Vertices[0], poly2.Vertices[1]);
    p.Add(new TriangleNet.Geometry.Contour(verts, 2), new Point(pointInHole.X, pointInHole.Y));
  }

  // picks a point on the right side of the given edge
  private Vector2 GetRightPointOfEdge(Vector2 a, Vector2 b)
  {
    Vector2 ab = b - a;
    var normal = Vector2.Normalize(new Vector2(-ab.Y, ab.X));
    var right = -0.01 * normal;
    var midPoint = Vector2.Lerp(a, b, 0.5f);
    return midPoint + right;
  }
}
