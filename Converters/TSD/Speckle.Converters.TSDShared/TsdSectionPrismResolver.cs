using Speckle.Common.StructuralExtrusion;
using TSD.API.Remoting.Sections;

namespace Speckle.Converters.TSDShared;

/// <summary>The unit prism for a TSD section, or the reason none could be built (the section geometry name, qualified).</summary>
public sealed record TsdSectionPrism(PrismTemplate? Template, string ShapeKey);

/// <summary>
/// Resolves a TSD section to a cached unit prism through its typed section interface (ENG-9048, ADR-0002). Every
/// result, including "unsupported", is cached per send by section name.
/// </summary>
public sealed class TsdSectionPrismResolver
{
  // Root radii and fillets are not modelled, so the catalog area sits a little under the host's.
  public const double AREA_TOLERANCE = 0.05;

  private readonly Dictionary<string, TsdSectionPrism> _cache = new(StringComparer.Ordinal);

  public TsdSectionPrism Resolve(ISection section)
  {
    string key = section.LongName ?? section.ShortName ?? section.SectionGeometry.ToString();
    if (_cache.TryGetValue(key, out var cached))
    {
      return cached;
    }

    var resolved = ResolveUncached(section);
    _cache[key] = resolved;
    return resolved;
  }

  private static TsdSectionPrism ResolveUncached(ISection section)
  {
    string shape = section.SectionGeometry.ToString();
    var profile = ToProfile(section);
    if (profile is null)
    {
      return new(null, shape);
    }

    var outline = SectionProfileCatalog.TryBuild(profile);
    if (outline is null)
    {
      return new(null, $"{shape}/invalid-dimensions");
    }

    double hostArea = section.CrossSectionalArea;
    if (double.IsNaN(hostArea) || hostArea <= 0)
    {
      return new(null, $"{shape}/no-host-area");
    }
    if (Math.Abs(outline.Area - hostArea) / hostArea > AREA_TOLERANCE)
    {
      return new(null, $"{shape}/area-mismatch");
    }

    var prism = PrismBuilder.TryBuildUnitPrism(outline);
    return prism is null ? new(null, $"{shape}/tessellation-failed") : new(prism, shape);
  }

  // Flipped parametric tees and angles are inverted shapes the catalog does not draw; they fall back.
  private static SectionProfile? ToProfile(ISection section) =>
    section switch
    {
      ISymmetricISection s => new ISectionProfile(
        s.Depth,
        s.Breadth,
        s.FlangeThickness,
        s.WebThickness,
        s.Breadth,
        s.FlangeThickness
      ),
      IAsymmetricBeamSection s => new ISectionProfile(
        s.Depth,
        s.TopFlangeBreadth,
        s.FlangeThickness,
        s.WebThickness,
        s.BottomFlangeBreadth,
        s.FlangeThickness
      ),
      IParametricISection s => s.IsFlippedVertically
        ? new ISectionProfile(
          s.Depth,
          s.BottomFlangeBreadth,
          s.BottomFlangeThickness,
          s.WebThickness,
          s.TopFlangeBreadth,
          s.TopFlangeThickness
        )
        : new ISectionProfile(
          s.Depth,
          s.TopFlangeBreadth,
          s.TopFlangeThickness,
          s.WebThickness,
          s.BottomFlangeBreadth,
          s.BottomFlangeThickness
        ),
      IChannel s => new ChannelProfile(s.Depth, s.Breadth, s.FlangeThickness, s.WebThickness),
      ITee s => new TeeProfile(s.Depth, s.Breadth, s.FlangeThickness, s.WebThickness),
      IParametricTSection { IsFlippedVertically: false, IsFlippedHorizontally: false } s => new TeeProfile(
        s.Depth,
        s.Breadth,
        s.FlangeThickness,
        s.WebThickness
      ),
      ISingleAngleSection s => new AngleProfile(s.LongLegLength, s.ShortLegLength, s.Thickness, s.Thickness),
      IParametricLSection { IsFlippedVertically: false, IsFlippedHorizontally: false } s => new AngleProfile(
        s.Depth,
        s.Breadth,
        s.FlangeThickness,
        s.WebThickness
      ),
      IRectangularHollowSection s => new RectangularHollowProfile(s.Depth, s.Breadth, s.WallThickness, s.WallThickness),
      ISquareHollowSection s => new RectangularHollowProfile(s.Depth, s.Breadth, s.WallThickness, s.WallThickness),
      IBoxSection s => new RectangularHollowProfile(s.Depth, s.Breadth, s.FlangeThickness, s.WebThickness),
      ICircularHollowSection s => new CircularHollowProfile(s.OuterDiameter, s.WallThickness),
      IRod s => new CircleProfile(s.OuterDiameter),
      IParametricCircularSection s => new CircleProfile(s.OuterDiameter),
      IBar s => new RectangleProfile(s.Depth, s.Thickness),
      IParametricRectangularSection s => new RectangleProfile(s.Depth, s.Breadth),
      ITimberBeamSection s => new RectangleProfile(s.Depth, s.Breadth),
      _ => null,
    };
}
