namespace Speckle.Common.SectionGeometry;

/// <summary>
/// One closed ring of a <see cref="SectionProfile"/>, as a range into the profile's point list.
/// </summary>
/// <remarks>
/// NOTE: every loop is stored counter-clockwise, holes included. Keeping holes in the same direction as the outer
/// ring means hole point i sits opposite outer point i, which is what makes the pipe and box cap triangulation
/// plain index arithmetic. <see cref="IsHole"/> is what tells the sweeper to walk this ring backwards instead.
/// </remarks>
public readonly struct ProfileLoop
{
  /// <summary>Creates a loop spanning <paramref name="count"/> points from <paramref name="start"/>.</summary>
  public ProfileLoop(int start, int count, bool isHole)
  {
    Start = start;
    Count = count;
    IsHole = isHole;
  }

  /// <summary>Index of this loop's first point in <see cref="SectionProfile.Points"/>.</summary>
  public int Start { get; }

  /// <summary>Points in this loop. The ring is implicitly closed.</summary>
  public int Count { get; }

  /// <summary>True when this ring bounds a void rather than the outside of the section.</summary>
  public bool IsHole { get; }
}
