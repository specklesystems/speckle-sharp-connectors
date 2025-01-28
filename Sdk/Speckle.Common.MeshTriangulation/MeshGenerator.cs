using Speckle.DoubleNumerics;

namespace Speckle.Common.MeshTriangulation;

public sealed class MeshGenerator
{
  private readonly IBaseTransformer _baseTransformer;
  private readonly ITriangulator _triangulator;

  public MeshGenerator(IBaseTransformer baseTransformer, ITriangulator triangulator)
  {
    _baseTransformer = baseTransformer;
    _triangulator = triangulator;
  }

  public Mesh3 TriangulateSurface(IReadOnlyList<Poly3> polygons)
  {
    _baseTransformer.SetTargetPlane(polygons[0]);
    var polygons2D = new List<Poly2>();
    foreach (var polygon in polygons)
    {
      polygons2D.Add(_baseTransformer.Poly3ToPoly2(polygon));
    }
    var mesh2 = _triangulator.Triangulate(polygons2D);

    return _baseTransformer.Mesh2ToMesh3(mesh2);
  }

  public Mesh3 ExtrudeMesh(IReadOnlyList<Poly3> polygons, double distance)
  {
    // TODO checks for polygons (first one should exist and should contain at least 3 vertices)

    var vertices = new List<Vector3>();
    var triangles = new List<int>();
    var tc = 0;

    // calculate normal
    var v1 = polygons[0].Vertices[1] - polygons[0].Vertices[0];
    var v2 = polygons[0].Vertices[2] - polygons[0].Vertices[0];
    var normal = Vector3.Normalize(Vector3.Cross(v1, v2));
    var offset = normal * distance;

    foreach (var polygon in polygons)
    {
      for (var curr = 0; curr < polygon.Vertices.Count; curr++)
      {
        var next = curr + 1;
        if (next >= polygon.Vertices.Count)
        {
          next = 0;
        }

        var p1 = polygon.Vertices[curr];
        var p2 = polygon.Vertices[next];
        var p3 = polygon.Vertices[curr] + offset;

        var p4 = polygon.Vertices[next];
        var p5 = polygon.Vertices[next] + offset;
        var p6 = polygon.Vertices[curr] + offset;

        vertices.Add(p1);
        vertices.Add(p2);
        vertices.Add(p3);
        vertices.Add(p4);
        vertices.Add(p5);
        vertices.Add(p6);

        triangles.Add(tc++);
        triangles.Add(tc++);
        triangles.Add(tc++);
        triangles.Add(tc++);
        triangles.Add(tc++);
        triangles.Add(tc++);
      }
    }

    // Add caps
    var cap = TriangulateSurface(polygons);
    foreach (var vertex in cap.Vertices)
    {
      vertices.Add(vertex);
      triangles.Add(tc++);
    }

    foreach (var vertex in cap.Vertices)
    {
      var op = vertex + offset;
      vertices.Add(op);
      triangles.Add(tc++);
    }

    return new Mesh3(vertices, triangles);
  }
}
