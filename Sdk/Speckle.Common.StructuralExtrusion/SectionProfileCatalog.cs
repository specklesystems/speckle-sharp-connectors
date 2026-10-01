using Speckle.DoubleNumerics;

namespace Speckle.Common.StructuralExtrusion;

/// <summary>
/// Turns a <see cref="SectionProfile"/> into its outline polygon. Shapes are drawn with +x as "up" (local 2) and
/// +y along local 3; asymmetric shapes put their web/vertical leg on the -y side and their flange/horizontal leg
/// at -x, matching the CSi section viewer.
/// </summary>
public static class SectionProfileCatalog
{
  public const int CIRCLE_SEGMENTS = 24;

  /// <summary>Null when a dimension is non-positive or the walls do not fit inside the envelope.</summary>
  public static ProfileOutline? TryBuild(SectionProfile profile)
  {
    if (profile is null)
    {
      throw new ArgumentNullException(nameof(profile));
    }

    return profile switch
    {
      RectangleProfile p => Positive(p.Depth, p.Width) ? ProfileOutline.TryCreate(Rectangle(p.Depth, p.Width)) : null,
      CircleProfile p => Positive(p.Diameter) ? ProfileOutline.TryCreate(Circle(p.Diameter / 2)) : null,
      ISectionProfile p => ISection(p),
      ChannelProfile p => Channel(p),
      TeeProfile p => Tee(p),
      AngleProfile p => Angle(p),
      RectangularHollowProfile p => RectangularHollow(p),
      CircularHollowProfile p => CircularHollow(p),
      _ => null,
    };
  }

  private static ProfileOutline? ISection(ISectionProfile p)
  {
    if (
      !Positive(
        p.Depth,
        p.TopFlangeWidth,
        p.TopFlangeThickness,
        p.WebThickness,
        p.BottomFlangeWidth,
        p.BottomFlangeThickness
      )
      || p.TopFlangeThickness + p.BottomFlangeThickness >= p.Depth
      || p.WebThickness >= Math.Min(p.TopFlangeWidth, p.BottomFlangeWidth)
    )
    {
      return null;
    }

    double top = p.Depth / 2;
    double bottom = -top;
    double topInner = top - p.TopFlangeThickness;
    double bottomInner = bottom + p.BottomFlangeThickness;
    double wt = p.TopFlangeWidth / 2;
    double wb = p.BottomFlangeWidth / 2;
    double tw = p.WebThickness / 2;

    return ProfileOutline.TryCreate([
      new(bottom, -wb),
      new(bottom, wb),
      new(bottomInner, wb),
      new(bottomInner, tw),
      new(topInner, tw),
      new(topInner, wt),
      new(top, wt),
      new(top, -wt),
      new(topInner, -wt),
      new(topInner, -tw),
      new(bottomInner, -tw),
      new(bottomInner, -wb),
    ]);
  }

  private static ProfileOutline? Channel(ChannelProfile p)
  {
    if (
      !Positive(p.Depth, p.FlangeWidth, p.FlangeThickness, p.WebThickness)
      || 2 * p.FlangeThickness >= p.Depth
      || p.WebThickness >= p.FlangeWidth
    )
    {
      return null;
    }

    double top = p.Depth / 2;
    double bottom = -top;
    double w = p.FlangeWidth / 2;
    double webInner = -w + p.WebThickness;

    return ProfileOutline.TryCreate([
      new(bottom, -w),
      new(bottom, w),
      new(bottom + p.FlangeThickness, w),
      new(bottom + p.FlangeThickness, webInner),
      new(top - p.FlangeThickness, webInner),
      new(top - p.FlangeThickness, w),
      new(top, w),
      new(top, -w),
    ]);
  }

  private static ProfileOutline? Tee(TeeProfile p)
  {
    if (
      !Positive(p.Depth, p.FlangeWidth, p.FlangeThickness, p.StemThickness)
      || p.FlangeThickness >= p.Depth
      || p.StemThickness >= p.FlangeWidth
    )
    {
      return null;
    }

    double top = p.Depth / 2;
    double bottom = -top;
    double flangeInner = top - p.FlangeThickness;
    double w = p.FlangeWidth / 2;
    double s = p.StemThickness / 2;

    return ProfileOutline.TryCreate([
      new(top, -w),
      new(top, w),
      new(flangeInner, w),
      new(flangeInner, s),
      new(bottom, s),
      new(bottom, -s),
      new(flangeInner, -s),
      new(flangeInner, -w),
    ]);
  }

  private static ProfileOutline? Angle(AngleProfile p)
  {
    if (
      !Positive(p.Depth, p.Width, p.HorizontalLegThickness, p.VerticalLegThickness)
      || p.HorizontalLegThickness >= p.Depth
      || p.VerticalLegThickness >= p.Width
    )
    {
      return null;
    }

    double top = p.Depth / 2;
    double bottom = -top;
    double w = p.Width / 2;
    double legInnerX = bottom + p.HorizontalLegThickness;
    double legInnerY = -w + p.VerticalLegThickness;

    return ProfileOutline.TryCreate([
      new(bottom, -w),
      new(bottom, w),
      new(legInnerX, w),
      new(legInnerX, legInnerY),
      new(top, legInnerY),
      new(top, -w),
    ]);
  }

  private static ProfileOutline? RectangularHollow(RectangularHollowProfile p)
  {
    if (
      !Positive(p.Depth, p.Width, p.FlangeThickness, p.WebThickness)
      || 2 * p.FlangeThickness >= p.Depth
      || 2 * p.WebThickness >= p.Width
    )
    {
      return null;
    }

    return ProfileOutline.TryCreate(
      Rectangle(p.Depth, p.Width),
      [Rectangle(p.Depth - 2 * p.FlangeThickness, p.Width - 2 * p.WebThickness)]
    );
  }

  private static ProfileOutline? CircularHollow(CircularHollowProfile p)
  {
    if (!Positive(p.OuterDiameter, p.WallThickness) || 2 * p.WallThickness >= p.OuterDiameter)
    {
      return null;
    }

    return ProfileOutline.TryCreate(Circle(p.OuterDiameter / 2), [Circle(p.OuterDiameter / 2 - p.WallThickness)]);
  }

  private static List<Vector2> Rectangle(double depth, double width)
  {
    double d = depth / 2;
    double w = width / 2;
    return [new(-d, -w), new(-d, w), new(d, w), new(d, -w)];
  }

  private static List<Vector2> Circle(double radius)
  {
    var points = new List<Vector2>(CIRCLE_SEGMENTS);
    for (int i = 0; i < CIRCLE_SEGMENTS; i++)
    {
      double angle = 2 * Math.PI * i / CIRCLE_SEGMENTS;
      points.Add(new(radius * Math.Cos(angle), radius * Math.Sin(angle)));
    }
    return points;
  }

  private static bool Positive(params double[] values)
  {
    foreach (double v in values)
    {
      if (double.IsNaN(v) || double.IsInfinity(v) || v <= 0)
      {
        return false;
      }
    }
    return true;
  }
}
