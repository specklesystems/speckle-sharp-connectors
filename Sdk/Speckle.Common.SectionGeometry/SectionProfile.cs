using Speckle.DoubleNumerics;

namespace Speckle.Common.SectionGeometry;

/// <summary>
/// The closed 2D outline of a section, plus a triangulation of its face.
/// </summary>
/// <remarks>
/// A profile depends on the section, never on the member using it, so resolve one per section name and sweep it for
/// every member carrying it. Everything expensive happens once, here, at construction.
/// Coordinates live in an abstract (u, v) plane: u runs along the width, v along the depth. Which host axis each
/// maps to is the resolver's business.
/// </remarks>
public sealed class SectionProfile
{
  private SectionProfile(
    IReadOnlyList<Vector2> points,
    IReadOnlyList<ProfileLoop> loops,
    IReadOnlyList<int> capTriangles,
    ProfileFidelity fidelity
  )
  {
    Points = points;
    Loops = loops;
    CapTriangles = capTriangles;
    Fidelity = fidelity;
    Bounds = ProfileBounds.Of(points);
    (Area, Centroid) = Measure(points, capTriangles, Bounds);
  }

  /// <summary>Every point of every loop, concatenated. Loops address ranges of this.</summary>
  public IReadOnlyList<Vector2> Points { get; }

  /// <summary>The closed rings. First is the outer boundary, any others are holes.</summary>
  public IReadOnlyList<ProfileLoop> Loops { get; }

  /// <summary>
  /// Triples of indices into <see cref="Points"/>, wound counter-clockwise in (u, v). Both end caps of every swept
  /// member reuse these, one reversed - which is why a sweep adds no vertices beyond the two rings.
  /// </summary>
  public IReadOnlyList<int> CapTriangles { get; }

  /// <summary>How faithfully this outline represents the host's section.</summary>
  public ProfileFidelity Fidelity { get; }

  /// <summary>Extents, which cardinal points are measured against.</summary>
  public ProfileBounds Bounds { get; }

  /// <summary>
  /// Area enclosed by the outline, from <see cref="CapTriangles"/>.
  /// </summary>
  /// <remarks>
  /// Both hosts report an authoritative area per section, so comparing the two catches transposed dimensions, wrong
  /// shape mappings, missed holes and unit slips in one number. NOTE: expect a few percent under-read on rolled
  /// steel - the catalogue area includes root radii this outline omits.
  /// </remarks>
  public double Area { get; }

  /// <summary>Area centroid. Cardinal point 10 in ETABS terms.</summary>
  public Vector2 Centroid { get; }

  /// <summary>Creates a profile from a single hole-free outline, ear-clipping the face.</summary>
  public static SectionProfile Create(IReadOnlyList<Vector2> outline, ProfileFidelity fidelity)
  {
    if (outline is null)
    {
      throw new ArgumentNullException(nameof(outline));
    }

    if (outline.Count < 3)
    {
      throw new ArgumentException("A section outline needs at least three points.", nameof(outline));
    }

    var loops = new[] { new ProfileLoop(0, outline.Count, false) };
    return new SectionProfile(outline, loops, EarClipper.Triangulate(outline, 0, outline.Count), fidelity);
  }

  /// <summary>
  /// Creates a profile from pre-built loops and an explicit face triangulation, for ringed shapes whose quad strips
  /// are known analytically and need no general triangulator.
  /// </summary>
  public static SectionProfile Create(
    IReadOnlyList<Vector2> points,
    IReadOnlyList<ProfileLoop> loops,
    IReadOnlyList<int> capTriangles,
    ProfileFidelity fidelity
  )
  {
    if (points is null)
    {
      throw new ArgumentNullException(nameof(points));
    }

    if (loops is null)
    {
      throw new ArgumentNullException(nameof(loops));
    }

    if (capTriangles is null)
    {
      throw new ArgumentNullException(nameof(capTriangles));
    }

    if (loops.Count == 0)
    {
      throw new ArgumentException("A section profile needs at least one loop.", nameof(loops));
    }

    if (capTriangles.Count % 3 != 0)
    {
      throw new ArgumentException("Cap triangles must come in triples.", nameof(capTriangles));
    }

    return new SectionProfile(points, loops, capTriangles, fidelity);
  }

  private static (double Area, Vector2 Centroid) Measure(
    IReadOnlyList<Vector2> points,
    IReadOnlyList<int> capTriangles,
    ProfileBounds bounds
  )
  {
    double twiceArea = 0;
    double weightedU = 0;
    double weightedV = 0;

    for (int i = 0; i + 2 < capTriangles.Count; i += 3)
    {
      Vector2 a = points[capTriangles[i]];
      Vector2 b = points[capTriangles[i + 1]];
      Vector2 c = points[capTriangles[i + 2]];

      double cross = ((b.X - a.X) * (c.Y - a.Y)) - ((b.Y - a.Y) * (c.X - a.X));
      twiceArea += cross;
      weightedU += cross * (a.X + b.X + c.X);
      weightedV += cross * (a.Y + b.Y + c.Y);
    }

    // fully degenerate outline - fall back to the box centre rather than dividing by zero
    if (Math.Abs(twiceArea) < 1e-12)
    {
      return (0, bounds.Center);
    }

    return (twiceArea / 2, new Vector2(weightedU / (3 * twiceArea), weightedV / (3 * twiceArea)));
  }
}
