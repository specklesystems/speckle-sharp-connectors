using Speckle.DoubleNumerics;

namespace Speckle.Common.StructuralExtrusion;

/// <summary>
/// A planar section outline in the profile's canonical frame: x along the depth (local 2) axis, y along the width
/// (local 3) axis, centroid at the origin. The outer contour winds counter-clockwise, holes clockwise.
/// </summary>
public sealed class ProfileOutline
{
  public IReadOnlyList<Vector2> Outer { get; }
  public IReadOnlyList<IReadOnlyList<Vector2>> Holes { get; }
  public double Area { get; }

  private ProfileOutline(IReadOnlyList<Vector2> outer, IReadOnlyList<IReadOnlyList<Vector2>> holes, double area)
  {
    Outer = outer;
    Holes = holes;
    Area = area;
  }

  /// <summary>Normalises winding, recentres on the area centroid; null when the net area is degenerate.</summary>
  public static ProfileOutline? TryCreate(
    IReadOnlyList<Vector2> outer,
    IReadOnlyList<IReadOnlyList<Vector2>>? holes = null
  )
  {
    if (outer is null)
    {
      throw new ArgumentNullException(nameof(outer));
    }
    if (outer.Count < 3)
    {
      return null;
    }

    var outerCcw = PolygonMath.WithWinding(outer, counterClockwise: true);
    double outerArea = PolygonMath.SignedArea(outerCcw);
    if (outerArea < PolygonMath.AREA_EPSILON)
    {
      return null;
    }

    double netArea = outerArea;
    var weightedCentroid = PolygonMath.Centroid(outerCcw, outerArea) * outerArea;
    var holesCw = new List<IReadOnlyList<Vector2>>();
    foreach (var hole in holes ?? Array.Empty<IReadOnlyList<Vector2>>())
    {
      if (hole.Count < 3)
      {
        return null;
      }
      var holeCw = PolygonMath.WithWinding(hole, counterClockwise: false);
      double holeArea = -PolygonMath.SignedArea(holeCw);
      if (holeArea < PolygonMath.AREA_EPSILON)
      {
        return null;
      }
      netArea -= holeArea;
      weightedCentroid -= PolygonMath.Centroid(holeCw, -holeArea) * holeArea;
      holesCw.Add(holeCw);
    }

    if (netArea < PolygonMath.AREA_EPSILON)
    {
      return null;
    }

    var shift = -(weightedCentroid / netArea);
    var centredHoles = new List<IReadOnlyList<Vector2>>(holesCw.Count);
    foreach (var hole in holesCw)
    {
      centredHoles.Add(PolygonMath.Translated(hole, shift));
    }

    return new ProfileOutline(PolygonMath.Translated(outerCcw, shift), centredHoles, netArea);
  }
}
