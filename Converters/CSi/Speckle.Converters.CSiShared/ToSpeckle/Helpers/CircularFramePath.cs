using Speckle.Common.StructuralExtrusion;
using Speckle.DoubleNumerics;

namespace Speckle.Converters.CSiShared.ToSpeckle.Helpers;

public sealed class CircularFramePath
{
  private const int MAX_SEGMENTS = 65536;
  private readonly Vector3 _center;
  private readonly Vector3 _radial;
  private readonly Vector3 _normal;
  private readonly double _radius;
  private readonly double _sweep;

  private CircularFramePath(Vector3 center, Vector3 radial, Vector3 normal, double radius, double sweep)
  {
    _center = center;
    _radial = radial;
    _normal = normal;
    _radius = radius;
    _sweep = sweep;
  }

  public static CircularFramePath? TryCreate(Vector3 start, Vector3 end, Vector3 through)
  {
    if (!Finite(start) || !Finite(end) || !Finite(through))
    {
      return null;
    }
    var u = end - start;
    var v = through - start;
    var cross = Vector3.Cross(u, v);
    double crossSquared = cross.LengthSquared();
    if (crossSquared <= 1e-24 * u.LengthSquared() * v.LengthSquared() || crossSquared == 0)
    {
      return null;
    }
    var center =
      start
      + (u.LengthSquared() * Vector3.Cross(v, cross) + v.LengthSquared() * Vector3.Cross(cross, u))
        / (2 * crossSquared);
    var radial = start - center;
    double radius = radial.Length();
    if (!Finite(center) || !Finite(radius) || radius <= 0)
    {
      return null;
    }
    radial /= radius;
    var normal = Vector3.Normalize(cross);
    var second = Vector3.Cross(normal, radial);
    double endAngle = PositiveAngle(end);
    double throughAngle = PositiveAngle(through);
    double sweep = throughAngle <= endAngle ? endAngle : endAngle - 2 * Math.PI;
    if (sweep == 0 || Math.Abs(sweep) >= 2 * Math.PI)
    {
      return null;
    }
    return new CircularFramePath(center, radial, normal, radius, sweep);

    double PositiveAngle(Vector3 point)
    {
      var relative = point - center;
      double angle = Math.Atan2(Vector3.Dot(relative, second), Vector3.Dot(relative, radial));
      return angle < 0 ? angle + 2 * Math.PI : angle;
    }
  }

  public PrismMesh? TrySweep(
    PrismTemplate template,
    LocalFrame analyticalFrame,
    Vector3 translation,
    Vector2 shift,
    double tolerance
  )
  {
    if (!Finite(analyticalFrame) || !Finite(shift))
    {
      return null;
    }
    var start = _center + _radius * _radial;
    var tangent = Math.Sign(_sweep) * Vector3.Cross(_normal, _radial);
    double initialAngle = Math.Atan2(
      Vector3.Dot(_normal, Vector3.Cross(analyticalFrame.ZAxis, tangent)),
      Vector3.Dot(analyticalFrame.ZAxis, tangent)
    );
    var transportedFrame = new LocalFrame(
      Rotate(analyticalFrame.XAxis, initialAngle),
      Rotate(analyticalFrame.YAxis, initialAngle),
      tangent
    );
    var placement = FramePhysicalPlacement.TryCreate(start, start + tangent, transportedFrame);
    if (placement is null || !Finite(translation) || !Finite(tolerance) || tolerance <= 0)
    {
      return null;
    }
    var frame = placement.Value.Frame;
    var points = new List<Vector2>();
    var pointIndices = new Dictionary<(double X, double Y), int>();
    int vertexCount = template.LocalVertices.Count / 3;
    var indices = new int[vertexCount];
    var ends = new int[vertexCount];
    double profileRadius = 0;
    for (int i = 0; i < vertexCount; i++)
    {
      var key = (template.LocalVertices[3 * i], template.LocalVertices[3 * i + 1]);
      if (!pointIndices.TryGetValue(key, out int index))
      {
        index = points.Count;
        pointIndices.Add(key, index);
        var point = new Vector2(key.Item1 + shift.X, key.Item2 + shift.Y);
        points.Add(point);
        profileRadius = Math.Max(profileRadius, point.Length());
      }
      indices[i] = index;
      ends[i] = template.LocalVertices[3 * i + 2] == 0 ? 0 : 1;
    }
    if (profileRadius >= _radius)
    {
      return null;
    }
    double edgeLength = 0;
    for (int i = 0; i < template.Faces.Count; i += 4)
    {
      int a = template.Faces[i + 1],
        b = template.Faces[i + 2],
        c = template.Faces[i + 3];
      if (ends[a] != ends[b] || ends[b] != ends[c])
      {
        edgeLength = Math.Max(
          edgeLength,
          Math.Max(
            (points[indices[a]] - points[indices[b]]).Length(),
            (points[indices[b]] - points[indices[c]]).Length()
          )
        );
        edgeLength = Math.Max(edgeLength, (points[indices[c]] - points[indices[a]]).Length());
      }
    }

    // ENG-10486: the bound includes triangle interpolation across rotating section edges, not only path sagitta.
    double maxAngle = Math.Min(
      Math.Sqrt(4 * tolerance / (_radius + profileRadius)),
      edgeLength == 0 ? Math.PI : 2 * tolerance / edgeLength
    );
    double requiredSegments = Math.Ceiling(Math.Abs(_sweep) / maxAngle);
    if (!Finite(requiredSegments) || requiredSegments > MAX_SEGMENTS)
    {
      return null;
    }
    int segments = Math.Max(1, (int)requiredSegments);
    var vertices = new List<double>((segments + 1) * points.Count * 3);
    for (int station = 0; station <= segments; station++)
    {
      double angle = _sweep * station / segments;
      var origin = _center + _radius * Rotate(_radial, angle) + translation;
      var depth = Rotate(frame.XAxis, angle);
      var width = Rotate(frame.YAxis, angle);
      foreach (var point in points)
      {
        var world = origin + point.X * depth + point.Y * width;
        vertices.Add(world.X);
        vertices.Add(world.Y);
        vertices.Add(world.Z);
      }
    }
    var faces = new List<int>();
    for (int i = 0; i < template.Faces.Count; i += 4)
    {
      int a = template.Faces[i + 1],
        b = template.Faces[i + 2],
        c = template.Faces[i + 3];
      bool cap = ends[a] == ends[b] && ends[b] == ends[c];
      if (cap)
      {
        int station = ends[a] * segments;
        AddTriangle(
          station * points.Count + indices[a],
          station * points.Count + indices[b],
          station * points.Count + indices[c]
        );
      }
      else
      {
        for (int station = 0; station < segments; station++)
        {
          AddTriangle(
            (station + ends[a]) * points.Count + indices[a],
            (station + ends[b]) * points.Count + indices[b],
            (station + ends[c]) * points.Count + indices[c]
          );
        }
      }
    }
    return new PrismMesh(vertices, faces);

    void AddTriangle(int a, int b, int c)
    {
      faces.Add(3);
      faces.Add(a);
      faces.Add(b);
      faces.Add(c);
    }
  }

  private Vector3 Rotate(Vector3 value, double angle) =>
    Math.Cos(angle) * value
    + Math.Sin(angle) * Vector3.Cross(_normal, value)
    + (1 - Math.Cos(angle)) * Vector3.Dot(_normal, value) * _normal;

  private static bool Finite(Vector3 value) => Finite(value.X) && Finite(value.Y) && Finite(value.Z);

  private static bool Finite(Vector2 value) => Finite(value.X) && Finite(value.Y);

  private static bool Finite(LocalFrame value) => Finite(value.XAxis) && Finite(value.YAxis) && Finite(value.ZAxis);

  private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
