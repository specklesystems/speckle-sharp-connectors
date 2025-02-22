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

  // creates a triangulated surface mesh from the given polygons
  // each polygon has to contain at least 3 points
  // the first polygon is the contour of the mesh, it has to be clockwise
  // the rest of the polygons define the holes, these have to be counterclockwise
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

  // creates an extruded triangle mesh from the given polygons
  // each polygon has to contain at least 3 points
  // the first polygon is the contour of the mesh, it has to be clockwise
  // the rest of the polygons define the holes, these have to be counterclockwise
  // the mesh will be extruded in the normal direction by the given height
  public Mesh3 ExtrudeMesh(IReadOnlyList<Poly3> polygons, double extrusionHeight)
  {
    if (polygons.Count < 1)
    {
      throw new ArgumentException("No polygon was provided for extrusion");
    }

    var vertices = new List<Vector3>();
    var triangles = new List<int>();
    var tc = 0;

    var normal = polygons[0].GetNormal();
    var offset = normal * extrusionHeight;

    // the contour has to be clockwise and the holes have to be counterclockwise
    // if any of the holes is clockwise then it has to be reversed
    for (var i = 1; i < polygons.Count; i++)
    {
      var polyNormal = polygons[i].GetNormal();
      if ((polyNormal + normal).Length() > 0.01f)
      {
        polygons[i].Reverse();
      }
    }

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

    var cap = TriangulateSurface(polygons);

    // Top cap triangles have to be in reverse order
    for (var i = 0; i < cap.Vertices.Count; i += 3)
    {
      vertices.Add(cap.Vertices[i + 2]);
      vertices.Add(cap.Vertices[i + 1]);
      vertices.Add(cap.Vertices[i]);

      triangles.Add(tc++);
      triangles.Add(tc++);
      triangles.Add(tc++);
    }

    // Bottom cap
    foreach (var vertex in cap.Vertices)
    {
      var op = vertex + offset;
      vertices.Add(op);
      triangles.Add(tc++);
    }

    return new Mesh3(vertices, triangles);
  }

  public Mesh3 ExtrudeMesh(IReadOnlyList<Poly3> polygons, Vector3 point)
  {
    var distance = GetDistanceToPlane(polygons[0], point);
    return ExtrudeMesh(polygons, distance);
  }

  public static double GetDistanceToPlane(Poly3 plane, Vector3 point)
  {
    var p1 = plane.Vertices[0];
    var p2 = plane.Vertices[1];
    var p3 = plane.Vertices[2];

    Vector3 v1 = p2 - p1;
    Vector3 v2 = p3 - p1;
    Vector3 normal = Vector3.Normalize(Vector3.Cross(v1, v2));

    var dot = -Vector3.Dot(normal, p1);
    var distance = Math.Abs(Vector3.Dot(normal, point) + dot);

    return distance;
  }
}
