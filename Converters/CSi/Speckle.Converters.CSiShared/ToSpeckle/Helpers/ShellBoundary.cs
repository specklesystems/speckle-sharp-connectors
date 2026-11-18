using Speckle.DoubleNumerics;
using Speckle.Sdk.Common;

namespace Speckle.Converters.CSiShared.ToSpeckle.Helpers;

internal sealed class ShellBoundary(List<Vector3> points, List<(int Edge, double Fraction)> sources, int cornerCount)
{
  public IReadOnlyList<Vector3> Points { get; } = points;

  public Vector3[] Interpolate(IReadOnlyList<Vector3> corners)
  {
    if (corners.Count != cornerCount)
    {
      throw new ArgumentException("Shell displacement count disagrees with source corners.", nameof(corners));
    }
    return sources
      .Select(s => Vector3.Lerp(corners[s.Edge], corners[(s.Edge + 1) % cornerCount], s.Fraction))
      .ToArray();
  }

  public double[] Interpolate(IReadOnlyList<double> corners)
  {
    if (corners.Count != cornerCount)
    {
      throw new ArgumentException("Shell thickness count disagrees with source corners.", nameof(corners));
    }
    return sources
      .Select(s => corners[s.Edge] + s.Fraction * (corners[(s.Edge + 1) % cornerCount] - corners[s.Edge]))
      .ToArray();
  }
}

internal static class ShellBoundaryReader
{
  private const int MAX_EDGE_SEGMENTS = 4096;
  private const int MAX_BOUNDARY_POINTS = 16384;
  private const double CHORD_TOLERANCE_MM = 0.25;

  public static ShellBoundary? TryRead(
    cAreaObj areas,
    string name,
    IReadOnlyList<Vector3> corners,
    string units,
    out string failure
  )
  {
    failure = "invalid-curve-data";
    int count = 0;
    int[] types = [],
      counts = [];
    double[] tension = [],
      x = [],
      y = [],
      z = [];
    if (areas.GetCurvedEdges(name, ref count, ref types, ref tension, ref counts, ref x, ref y, ref z) != 0)
    {
      failure = "curve-read-failed";
      return null;
    }
    if (
      count != corners.Count
      || count < 3
      || count > MAX_BOUNDARY_POINTS
      || corners.Any(p => !Finite(p))
      || !ValidArrays(count, types, counts, tension, x, y, z)
    )
    {
      return null;
    }
    double tolerance = CHORD_TOLERANCE_MM / Units.GetConversionFactor(units, Units.Millimeters);
    if (!Finite(tolerance) || tolerance <= 0 || units == Units.None)
    {
      failure = "curve-units-unsupported";
      return null;
    }
    var points = new List<Vector3>(count);
    var sources = new List<(int Edge, double Fraction)>(count);
    int control = 0;
    for (int edge = 0; edge < count; edge++)
    {
      points.Add(corners[edge]);
      sources.Add((edge, 0));
      if (types[edge] == 0)
      {
        if (counts[edge] != 0)
        {
          return null;
        }
        continue;
      }
      if (types[edge] != 1)
      {
        failure = "unsupported-curve-type";
        return null;
      }
      if (counts[edge] != 3)
      {
        return null;
      }
      var controls = Enumerable.Range(control, 3).Select(i => new Vector3(x[i], y[i], z[i])).ToArray();
      control += 3;
      if (controls.Any(p => !Finite(p)))
      {
        return null;
      }
      if (!Planar(corners, controls[2], tolerance * 1e-3))
      {
        failure = "nonplanar-curved-boundary";
        return null;
      }
      // ENG-10466/ENG-10491: ETABS normalizes cached edge endpoints to active source corners.
      var arc = CircularArc.TryCreate(corners[edge], corners[(edge + 1) % count], controls[2]);
      if (arc is null)
      {
        failure = "degenerate-circular-edge";
        return null;
      }
      double step = 4 * Math.Asin(Math.Sqrt(Math.Min(tolerance / (2 * arc.Radius), 0.5)));
      double firstSegments = Math.Ceiling(Math.Abs(arc.Sweep * arc.ThroughFraction) / step);
      double secondSegments = Math.Ceiling(Math.Abs(arc.Sweep * (1 - arc.ThroughFraction)) / step);
      double segments = firstSegments + secondSegments;
      if (!Finite(segments) || segments < 1 || segments > MAX_EDGE_SEGMENTS)
      {
        failure = "curve-tessellation-limit";
        return null;
      }
      for (int i = 1; i <= (int)firstSegments; i++)
      {
        AddSample(arc.ThroughFraction * i / firstSegments, i == (int)firstSegments);
      }
      for (int i = 1; i < (int)secondSegments; i++)
      {
        AddSample(arc.ThroughFraction + (1 - arc.ThroughFraction) * i / secondSegments, false);
      }
      if (points.Count + count - edge - 1 > MAX_BOUNDARY_POINTS)
      {
        failure = "curve-tessellation-limit";
        return null;
      }

      void AddSample(double fraction, bool through)
      {
        points.Add(through ? controls[2] : arc.PointAt(fraction));
        sources.Add((edge, fraction));
      }
    }
    failure = string.Empty;
    return new ShellBoundary(points, sources, count);
  }

  private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

  private static bool Finite(Vector3 p) => Finite(p.X) && Finite(p.Y) && Finite(p.Z);

  private static bool Planar(IReadOnlyList<Vector3> corners, Vector3 through, double tolerance)
  {
    var normal = Vector3.Zero;
    for (int i = 0; i < corners.Count; i++)
    {
      normal += Vector3.Cross(corners[i] - corners[0], corners[(i + 1) % corners.Count] - corners[0]);
    }
    double length = normal.Length();
    return Finite(length)
      && length > 0
      && corners.Append(through).All(p => Math.Abs(Vector3.Dot(p - corners[0], normal / length)) <= tolerance);
  }

  private static bool ValidArrays(
    int count,
    int[] types,
    int[] counts,
    double[] tension,
    double[] x,
    double[] y,
    double[] z
  ) =>
    types.Length == count
    && counts.Length == count
    && tension.Length == count
    && counts.All(n => n >= 0)
    && counts.Sum(n => (long)n) == x.Length
    && x.Length == y.Length
    && x.Length == z.Length;

  private sealed record CircularArc(
    Vector3 Centre,
    Vector3 StartRadius,
    Vector3 TangentRadius,
    double Sweep,
    double ThroughFraction
  )
  {
    public double Radius => StartRadius.Length();

    public Vector3 PointAt(double fraction) =>
      Centre + Math.Cos(Sweep * fraction) * StartRadius + Math.Sin(Sweep * fraction) * TangentRadius;

    public static CircularArc? TryCreate(Vector3 start, Vector3 end, Vector3 through)
    {
      var chord = end - start;
      double length = chord.Length();
      if (!Finite(length) || length <= 0)
      {
        return null;
      }
      var xAxis = chord / length;
      var delta = through - start;
      var perpendicular = delta - Vector3.Dot(delta, xAxis) * xAxis;
      double height = perpendicular.Length();
      if (!Finite(height) || height <= length * 1e-12)
      {
        return null;
      }
      var yAxis = perpendicular / height;
      var normal = Vector3.Cross(xAxis, yAxis);
      double centreY = (delta.LengthSquared() - length * Vector3.Dot(delta, xAxis)) / (2 * height);
      var centre = start + length / 2 * xAxis + centreY * yAxis;
      var radius = start - centre;
      var tangent = Vector3.Cross(normal, radius);
      if (!Finite(centre) || !Finite(radius.Length()) || radius.Length() <= 0)
      {
        return null;
      }
      double endAngle = PositiveAngle(end);
      double throughAngle = PositiveAngle(through);
      double sweep = throughAngle < endAngle ? endAngle : endAngle - 2 * Math.PI;
      double throughSweep = sweep > 0 ? throughAngle : throughAngle - 2 * Math.PI;
      double fraction = throughSweep / sweep;
      return Finite(fraction) && fraction > 0 && fraction < 1
        ? new CircularArc(centre, radius, tangent, sweep, fraction)
        : null;

      double PositiveAngle(Vector3 point)
      {
        double angle = Math.Atan2(Vector3.Dot(point - centre, tangent), Vector3.Dot(point - centre, radius));
        return angle < 0 ? angle + 2 * Math.PI : angle;
      }
    }
  }
}
