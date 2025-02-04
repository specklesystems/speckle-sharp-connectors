using Speckle.DoubleNumerics;

namespace Speckle.Common.MeshTriangulation;

public class LibTessTriangulator : ITriangulator
{
  public Mesh2 Triangulate(IReadOnlyList<Poly2> polygons)
  {
    var tess = new LibTessDotNet.Tess();

    for (int polygonIndex = 0; polygonIndex < polygons.Count; polygonIndex++)
    {
      var numPoints = polygons[polygonIndex].Vertices.Count;
      var contour = new LibTessDotNet.ContourVertex[numPoints];
      for (int vertexIndex = 0; vertexIndex < numPoints; vertexIndex++)
      {
        var v = polygons[polygonIndex].Vertices[vertexIndex];
        contour[vertexIndex].Position = new LibTessDotNet.Vec3((float)v.X, (float)v.Y, 0);
      }

      // the outer contour has to be in clockwise order
      var orientation =
        polygonIndex == 0
          ? LibTessDotNet.ContourOrientation.Clockwise
          : LibTessDotNet.ContourOrientation.CounterClockwise;
      tess.AddContour(contour, orientation);
    }

    tess.Tessellate();

    var vertices = new List<Vector2>();
    var triangles = new List<int>();
    int tc = 0;

    int numTriangles = tess.ElementCount;
    for (int i = 0; i < numTriangles; i++)
    {
      var v1 = tess.Vertices[tess.Elements[i * 3]].Position;
      var v2 = tess.Vertices[tess.Elements[i * 3 + 1]].Position;
      var v3 = tess.Vertices[tess.Elements[i * 3 + 2]].Position;

      vertices.Add(new Vector2(v1.X, v1.Y));
      vertices.Add(new Vector2(v2.X, v2.Y));
      vertices.Add(new Vector2(v3.X, v3.Y));

      triangles.Add(tc++);
      triangles.Add(tc++);
      triangles.Add(tc++);
    }

    return new Mesh2(vertices, triangles);
  }
}
