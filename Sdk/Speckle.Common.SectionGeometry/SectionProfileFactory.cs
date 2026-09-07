using Speckle.DoubleNumerics;

namespace Speckle.Common.SectionGeometry;

/// <summary>
/// Builds the section outlines that cover the overwhelming majority of members in a real building model.
/// </summary>
/// <remarks>
/// Parameter names follow the CSi vocabulary, it being the more complete of the two - depth is the extent along v,
/// width the extent along u. TSD's typed section interfaces map onto the same arguments under other names.
/// NOTE: outlines are built centred on their own bounding box. Insertion points are applied at sweep time, not
/// baked in here, because two members sharing a section can be inserted differently.
/// NOTE: dimensions must be finite and positive or we throw. Dimensions that are positive but mutually impossible
/// (a web thicker than its flange) are clamped, so odd data still yields a valid mesh instead of failing a send.
/// </remarks>
public static class SectionProfileFactory
{
  /// <summary>Segments used to approximate circular outlines. Ample for a display mesh.</summary>
  public const int DEFAULT_CIRCLE_SEGMENTS = 16;

  /// <summary>Most of an enclosing dimension a thickness may take before it is clamped.</summary>
  private const double THICKNESS_LIMIT = 0.999;

  /// <summary>A solid rectangular section.</summary>
  public static SectionProfile Rectangle(double depth, double width) =>
    SectionProfile.Create(RectangleOutline(depth, width), ProfileFidelity.Exact);

  /// <summary>
  /// The bounding rectangle of a section we can't draw but whose overall dimensions we know. Right size, wrong
  /// shape, and flagged as such.
  /// </summary>
  public static SectionProfile BoundingEnvelope(double depth, double width) =>
    SectionProfile.Create(RectangleOutline(depth, width), ProfileFidelity.BoundingEnvelope);

  /// <summary>A solid circular section.</summary>
  public static SectionProfile Circle(double diameter, int segments = DEFAULT_CIRCLE_SEGMENTS)
  {
    double radius = Positive(diameter, nameof(diameter)) / 2;
    return SectionProfile.Create(Ring(radius, RequireSegments(segments)), ProfileFidelity.Exact);
  }

  /// <summary>A circular hollow section.</summary>
  public static SectionProfile Pipe(
    double outerDiameter,
    double wallThickness,
    int segments = DEFAULT_CIRCLE_SEGMENTS
  )
  {
    double outerRadius = Positive(outerDiameter, nameof(outerDiameter)) / 2;
    double thickness = Fit(Positive(wallThickness, nameof(wallThickness)), outerRadius);
    int count = RequireSegments(segments);

    var points = new List<Vector2>(count * 2);
    points.AddRange(Ring(outerRadius, count));
    points.AddRange(Ring(outerRadius - thickness, count));

    return SectionProfile.Create(points, RingLoops(0, count), RingCap(count, 0, count), ProfileFidelity.Exact);
  }

  /// <summary>A rectangular hollow section. Flange is the horizontal wall, web the vertical one.</summary>
  public static SectionProfile Box(double depth, double width, double flangeThickness, double webThickness)
  {
    double halfDepth = Positive(depth, nameof(depth)) / 2;
    double halfWidth = Positive(width, nameof(width)) / 2;
    double flange = Fit(Positive(flangeThickness, nameof(flangeThickness)), halfDepth);
    double web = Fit(Positive(webThickness, nameof(webThickness)), halfWidth);

    var points = new List<Vector2>(8);
    points.AddRange(RectangleCorners(halfDepth, halfWidth));
    points.AddRange(RectangleCorners(halfDepth - flange, halfWidth - web));

    return SectionProfile.Create(points, RingLoops(0, 4), RingCap(4, 0, 4), ProfileFidelity.Exact);
  }

  /// <summary>An I-shape. Pass the same width and thickness for both flanges to get a symmetric one.</summary>
  public static SectionProfile ISection(
    double depth,
    double topFlangeWidth,
    double topFlangeThickness,
    double webThickness,
    double bottomFlangeWidth,
    double bottomFlangeThickness
  )
  {
    double halfDepth = Positive(depth, nameof(depth)) / 2;
    double halfTop = Positive(topFlangeWidth, nameof(topFlangeWidth)) / 2;
    double halfBottom = Positive(bottomFlangeWidth, nameof(bottomFlangeWidth)) / 2;
    double topFlange = Fit(Positive(topFlangeThickness, nameof(topFlangeThickness)), halfDepth);
    double bottomFlange = Fit(Positive(bottomFlangeThickness, nameof(bottomFlangeThickness)), halfDepth);
    double halfWeb = Fit(Positive(webThickness, nameof(webThickness)), Math.Min(topFlangeWidth, bottomFlangeWidth)) / 2;

    double webBottom = -halfDepth + bottomFlange;
    double webTop = halfDepth - topFlange;

    var outline = new List<Vector2>(12)
    {
      new(-halfBottom, -halfDepth),
      new(halfBottom, -halfDepth),
      new(halfBottom, webBottom),
      new(halfWeb, webBottom),
      new(halfWeb, webTop),
      new(halfTop, webTop),
      new(halfTop, halfDepth),
      new(-halfTop, halfDepth),
      new(-halfTop, webTop),
      new(-halfWeb, webTop),
      new(-halfWeb, webBottom),
      new(-halfBottom, webBottom),
    };

    return SectionProfile.Create(outline, ProfileFidelity.Exact);
  }

  /// <summary>A channel, web on the negative-u side and flanges opening toward positive u.</summary>
  public static SectionProfile Channel(double depth, double width, double flangeThickness, double webThickness)
  {
    double halfDepth = Positive(depth, nameof(depth)) / 2;
    double halfWidth = Positive(width, nameof(width)) / 2;
    double flange = Fit(Positive(flangeThickness, nameof(flangeThickness)), halfDepth);
    double web = Fit(Positive(webThickness, nameof(webThickness)), width);

    double webFace = -halfWidth + web;
    double flangeBottom = -halfDepth + flange;
    double flangeTop = halfDepth - flange;

    var outline = new List<Vector2>(8)
    {
      new(-halfWidth, -halfDepth),
      new(halfWidth, -halfDepth),
      new(halfWidth, flangeBottom),
      new(webFace, flangeBottom),
      new(webFace, flangeTop),
      new(halfWidth, flangeTop),
      new(halfWidth, halfDepth),
      new(-halfWidth, halfDepth),
    };

    return SectionProfile.Create(outline, ProfileFidelity.Exact);
  }

  /// <summary>A tee, flange at the top and web hanging below.</summary>
  public static SectionProfile Tee(double depth, double width, double flangeThickness, double webThickness)
  {
    double halfDepth = Positive(depth, nameof(depth)) / 2;
    double halfWidth = Positive(width, nameof(width)) / 2;
    double flange = Fit(Positive(flangeThickness, nameof(flangeThickness)), depth);
    double halfWeb = Fit(Positive(webThickness, nameof(webThickness)), width) / 2;

    double flangeUnderside = halfDepth - flange;

    var outline = new List<Vector2>(8)
    {
      new(-halfWeb, -halfDepth),
      new(halfWeb, -halfDepth),
      new(halfWeb, flangeUnderside),
      new(halfWidth, flangeUnderside),
      new(halfWidth, halfDepth),
      new(-halfWidth, halfDepth),
      new(-halfWidth, flangeUnderside),
      new(-halfWeb, flangeUnderside),
    };

    return SectionProfile.Create(outline, ProfileFidelity.Exact);
  }

  /// <summary>An angle, corner at the negative-u, negative-v end with both legs running off it.</summary>
  public static SectionProfile Angle(
    double depth,
    double width,
    double horizontalLegThickness,
    double verticalLegThickness
  )
  {
    double halfDepth = Positive(depth, nameof(depth)) / 2;
    double halfWidth = Positive(width, nameof(width)) / 2;
    double horizontal = Fit(Positive(horizontalLegThickness, nameof(horizontalLegThickness)), depth);
    double vertical = Fit(Positive(verticalLegThickness, nameof(verticalLegThickness)), width);

    double legTop = -halfDepth + horizontal;
    double legFace = -halfWidth + vertical;

    var outline = new List<Vector2>(6)
    {
      new(-halfWidth, -halfDepth),
      new(halfWidth, -halfDepth),
      new(halfWidth, legTop),
      new(legFace, legTop),
      new(legFace, halfDepth),
      new(-halfWidth, halfDepth),
    };

    return SectionProfile.Create(outline, ProfileFidelity.Exact);
  }

  private static List<Vector2> RectangleOutline(double depth, double width) =>
    RectangleCorners(Positive(depth, nameof(depth)) / 2, Positive(width, nameof(width)) / 2);

  private static List<Vector2> RectangleCorners(double halfDepth, double halfWidth) =>
    new(4)
    {
      new(-halfWidth, -halfDepth),
      new(halfWidth, -halfDepth),
      new(halfWidth, halfDepth),
      new(-halfWidth, halfDepth),
    };

  private static List<Vector2> Ring(double radius, int segments)
  {
    var points = new List<Vector2>(segments);
    for (int i = 0; i < segments; i++)
    {
      double angle = 2 * Math.PI * i / segments;
      points.Add(new Vector2(radius * Math.Cos(angle), radius * Math.Sin(angle)));
    }

    return points;
  }

  private static List<ProfileLoop> RingLoops(int outerStart, int count) =>
    new(2) { new ProfileLoop(outerStart, count, false), new ProfileLoop(outerStart + count, count, true) };

  /// <summary>
  /// Caps a ringed shape as one quad per sector. Because the hole ring is stored counter-clockwise too, its point i
  /// sits opposite outer point i and the whole thing is index arithmetic.
  /// </summary>
  private static List<int> RingCap(int count, int outerStart, int innerStart)
  {
    var triangles = new List<int>(count * 6);
    for (int i = 0; i < count; i++)
    {
      int next = (i + 1) % count;
      int outerHere = outerStart + i;
      int outerNext = outerStart + next;
      int innerHere = innerStart + i;
      int innerNext = innerStart + next;

      triangles.Add(outerHere);
      triangles.Add(outerNext);
      triangles.Add(innerNext);

      triangles.Add(outerHere);
      triangles.Add(innerNext);
      triangles.Add(innerHere);
    }

    return triangles;
  }

  private static double Positive(double value, string name)
  {
    if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
    {
      throw new ArgumentOutOfRangeException(name, value, "Section dimensions must be finite and positive.");
    }

    return value;
  }

  private static double Fit(double thickness, double limit) => Math.Min(thickness, limit * THICKNESS_LIMIT);

  private static int RequireSegments(int segments)
  {
    if (segments < 3)
    {
      throw new ArgumentOutOfRangeException(
        nameof(segments),
        segments,
        "A circular outline needs at least three segments."
      );
    }

    return segments;
  }
}
