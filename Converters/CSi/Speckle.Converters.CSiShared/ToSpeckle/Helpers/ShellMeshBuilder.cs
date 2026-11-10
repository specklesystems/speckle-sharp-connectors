using Speckle.Common.MeshTriangulation;
using Speckle.Common.StructuralExtrusion;
using Speckle.DoubleNumerics;

namespace Speckle.Converters.CSiShared.ToSpeckle.Helpers;

internal static class ShellMeshBuilder
{
  public static PrismMesh? TryBuild(
    IReadOnlyList<Vector3> outline,
    Vector3 normal,
    ShellGeometryAssignments assignments
  )
  {
    if (
      outline.Count < 3
      || outline.Count != assignments.Displacements.Length
      || outline.Count != assignments.Thicknesses.Length
    )
    {
      return null;
    }
    var reference = Math.Abs(normal.Z) < 0.9 ? Vector3.UnitZ : Vector3.UnitX;
    var frame = LocalFrame.TryCreate(normal, reference, 0);
    if (frame is null)
    {
      return null;
    }
    normal = frame.Value.ZAxis;
    int count = outline.Count;
    var footprint = new List<Vector2>(count);
    var cornerByPosition = new Dictionary<(float X, float Y), int>();
    for (int i = 0; i < count; i++)
    {
      var delta = outline[i] + assignments.Displacements[i] - outline[0] - assignments.Displacements[0];
      var point = new Vector2(Vector3.Dot(delta, frame.Value.XAxis), Vector3.Dot(delta, frame.Value.YAxis));
      var key = ((float)point.X, (float)point.Y);
      if (cornerByPosition.TryGetValue(key, out _))
      {
        return null;
      }
      cornerByPosition.Add(key, i);
      footprint.Add(point);
    }
    double area = 0;
    for (int i = 0; i < count; i++)
    {
      var next = footprint[(i + 1) % count];
      area += footprint[i].X * next.Y - next.X * footprint[i].Y;
    }
    if (Math.Abs(area) < 1e-12)
    {
      return null;
    }
    bool forward = area > 0;
    var cap = new LibTessTriangulator().Triangulate([new Poly2(footprint)]);
    if (cap.Triangles.Count == 0)
    {
      return null;
    }
    var vertices = new List<double>(count * 6);
    for (int layer = -1; layer <= 1; layer += 2)
    {
      for (int i = 0; i < count; i++)
      {
        var point = outline[i] + assignments.Displacements[i] + normal * (layer * assignments.Thicknesses[i] / 2);
        vertices.Add(point.X);
        vertices.Add(point.Y);
        vertices.Add(point.Z);
      }
    }
    var faces = new List<int>();
    for (int i = 0; i < count; i++)
    {
      int a = forward ? i : (i + 1) % count;
      int b = forward ? (i + 1) % count : i;
      faces.AddRange([3, a, b, count + b, 3, a, count + b, count + a]);
    }
    for (int i = 0; i < cap.Triangles.Count; i += 3)
    {
      var p = cap.Vertices[cap.Triangles[i]];
      var q = cap.Vertices[cap.Triangles[i + 1]];
      var r = cap.Vertices[cap.Triangles[i + 2]];
      if (
        !cornerByPosition.TryGetValue(((float)p.X, (float)p.Y), out int a)
        || !cornerByPosition.TryGetValue(((float)q.X, (float)q.Y), out int b)
        || !cornerByPosition.TryGetValue(((float)r.X, (float)r.Y), out int c)
      )
      {
        // ENG-10472: Tessellator-created intersections have no saved corner displacement or thickness.
        return null;
      }
      if ((q.X - p.X) * (r.Y - p.Y) - (r.X - p.X) * (q.Y - p.Y) < 0)
      {
        (b, c) = (c, b);
      }
      faces.AddRange([3, count + a, count + b, count + c, 3, a, c, b]);
    }
    return IsClosed(faces) ? new PrismMesh(vertices, faces) : null;
  }

  private static bool IsClosed(IReadOnlyList<int> faces)
  {
    var edges = new Dictionary<(int Start, int End), (int Count, int Orientation)>();
    for (int i = 0; i < faces.Count; i += 4)
    {
      for (int edge = 0; edge < 3; edge++)
      {
        int a = faces[i + 1 + edge];
        int b = faces[i + 1 + (edge + 1) % 3];
        var key = a < b ? (a, b) : (b, a);
        edges.TryGetValue(key, out var previous);
        edges[key] = (previous.Count + 1, previous.Orientation + (a < b ? 1 : -1));
      }
    }
    return edges.Values.All(edge => edge.Count == 2 && edge.Orientation == 0);
  }
}
