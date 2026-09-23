namespace Speckle.Common.StructuralExtrusion;

public enum SectionShape
{
  Rectangle,
  Circle,
  ISection,
  Channel,
  Tee,
  Angle,
  RectangularHollow,
  CircularHollow,
}

/// <summary>
/// Host-agnostic cross-section description (ENG-9048, ADR-0002). All lengths share one unit; the catalog never
/// converts. Depth runs along the member's local 2 (major) axis, width along local 3.
/// </summary>
public abstract record SectionProfile
{
  public abstract SectionShape Shape { get; }
}

public sealed record RectangleProfile(double Depth, double Width) : SectionProfile
{
  public override SectionShape Shape => SectionShape.Rectangle;
}

public sealed record CircleProfile(double Diameter) : SectionProfile
{
  public override SectionShape Shape => SectionShape.Circle;
}

public sealed record ISectionProfile(
  double Depth,
  double TopFlangeWidth,
  double TopFlangeThickness,
  double WebThickness,
  double BottomFlangeWidth,
  double BottomFlangeThickness
) : SectionProfile
{
  public override SectionShape Shape => SectionShape.ISection;
}

public sealed record ChannelProfile(double Depth, double FlangeWidth, double FlangeThickness, double WebThickness)
  : SectionProfile
{
  public override SectionShape Shape => SectionShape.Channel;
}

public sealed record TeeProfile(double Depth, double FlangeWidth, double FlangeThickness, double StemThickness)
  : SectionProfile
{
  public override SectionShape Shape => SectionShape.Tee;
}

/// <summary>The vertical leg spans <paramref name="Depth"/>; the horizontal leg spans <paramref name="Width"/>.</summary>
public sealed record AngleProfile(
  double Depth,
  double Width,
  double HorizontalLegThickness,
  double VerticalLegThickness
) : SectionProfile
{
  public override SectionShape Shape => SectionShape.Angle;
}

public sealed record RectangularHollowProfile(double Depth, double Width, double FlangeThickness, double WebThickness)
  : SectionProfile
{
  public override SectionShape Shape => SectionShape.RectangularHollow;
}

public sealed record CircularHollowProfile(double OuterDiameter, double WallThickness) : SectionProfile
{
  public override SectionShape Shape => SectionShape.CircularHollow;
}
