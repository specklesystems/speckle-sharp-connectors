using Speckle.DoubleNumerics;

namespace Speckle.Common.SectionGeometry;

/// <summary>
/// Axis-aligned extents of a <see cref="SectionProfile"/> in its own (u, v) plane.
/// </summary>
/// <remarks>
/// This is what cardinal points are measured against - ETABS names insertion points as positions on the section's
/// bounding box, so applying one is arithmetic on these four numbers.
/// </remarks>
public readonly struct ProfileBounds
{
  /// <summary>Creates bounds from explicit extents.</summary>
  public ProfileBounds(double minU, double maxU, double minV, double maxV)
  {
    MinU = minU;
    MaxU = maxU;
    MinV = minV;
    MaxV = maxV;
  }

  /// <summary>Lowest coordinate along the width axis.</summary>
  public double MinU { get; }

  /// <summary>Highest coordinate along the width axis.</summary>
  public double MaxU { get; }

  /// <summary>Lowest coordinate along the depth axis.</summary>
  public double MinV { get; }

  /// <summary>Highest coordinate along the depth axis.</summary>
  public double MaxV { get; }

  /// <summary>Overall width, along u.</summary>
  public double Width => MaxU - MinU;

  /// <summary>Overall depth, along v.</summary>
  public double Depth => MaxV - MinV;

  /// <summary>Midpoint of the bounding box.</summary>
  public Vector2 Center => new((MinU + MaxU) / 2, (MinV + MaxV) / 2);

  /// <summary>Bounds of a point set.</summary>
  public static ProfileBounds Of(IReadOnlyList<Vector2> points)
  {
    if (points is null)
    {
      throw new ArgumentNullException(nameof(points));
    }

    if (points.Count == 0)
    {
      return new ProfileBounds(0, 0, 0, 0);
    }

    double minU = double.MaxValue,
      maxU = double.MinValue,
      minV = double.MaxValue,
      maxV = double.MinValue;

    for (int i = 0; i < points.Count; i++)
    {
      Vector2 point = points[i];
      minU = Math.Min(minU, point.X);
      maxU = Math.Max(maxU, point.X);
      minV = Math.Min(minV, point.Y);
      maxV = Math.Max(maxV, point.Y);
    }

    return new ProfileBounds(minU, maxU, minV, maxV);
  }
}
