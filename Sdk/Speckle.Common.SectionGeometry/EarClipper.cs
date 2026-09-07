using Speckle.DoubleNumerics;

namespace Speckle.Common.SectionGeometry;

/// <summary>
/// Ear-clipping triangulation of a single simple polygon, used to cap hole-free profiles.
/// </summary>
/// <remarks>
/// Outlines are 4-16 points and this runs once per section, never per member, so the naive O(n²) sweep is the right
/// trade for code that is easy to check. Ringed shapes (pipe, box) bring their own cap triangles instead.
/// </remarks>
internal static class EarClipper
{
  /// <summary>
  /// Triangulates <paramref name="count"/> points from <paramref name="start"/>, returning index triples wound
  /// counter-clockwise.
  /// </summary>
  public static List<int> Triangulate(IReadOnlyList<Vector2> points, int start, int count)
  {
    var triangles = new List<int>(Math.Max(0, (count - 2) * 3));
    if (count < 3)
    {
      return triangles;
    }

    // work on a mutable ring of absolute indices, forced counter-clockwise so "convex" means one thing below
    var ring = new List<int>(count);
    for (int i = 0; i < count; i++)
    {
      ring.Add(start + i);
    }

    if (SignedArea(points, ring) < 0)
    {
      ring.Reverse();
    }

    int attemptsLeft = ring.Count; // a full pass with nothing clipped means the outline isn't simple
    int cursor = 0;

    while (ring.Count > 3)
    {
      int previous = ring[(cursor + ring.Count - 1) % ring.Count];
      int current = ring[cursor];
      int next = ring[(cursor + 1) % ring.Count];

      if (IsEar(points, ring, previous, current, next))
      {
        triangles.Add(previous);
        triangles.Add(current);
        triangles.Add(next);
        ring.RemoveAt(cursor);
        if (cursor >= ring.Count)
        {
          cursor = 0;
        }

        attemptsLeft = ring.Count;
        continue;
      }

      cursor = (cursor + 1) % ring.Count;
      attemptsLeft--;
      if (attemptsLeft <= 0)
      {
        // self-intersecting or degenerate. a fan still closes the cap, and a slightly wrong display mesh beats a
        // send that throws
        triangles.AddRange(Fan(ring));
        return triangles;
      }
    }

    triangles.Add(ring[0]);
    triangles.Add(ring[1]);
    triangles.Add(ring[2]);
    return triangles;
  }

  private static bool IsEar(IReadOnlyList<Vector2> points, List<int> ring, int previous, int current, int next)
  {
    Vector2 a = points[previous];
    Vector2 b = points[current];
    Vector2 c = points[next];

    // reflex vertices are never ears
    if (Cross(b - a, c - b) <= 0)
    {
      return false;
    }

    foreach (int index in ring)
    {
      if (index == previous || index == current || index == next)
      {
        continue;
      }

      if (IsStrictlyInside(points[index], a, b, c))
      {
        return false;
      }
    }

    return true;
  }

  // strict, so duplicate or collinear points don't veto an otherwise valid ear
  private static bool IsStrictlyInside(Vector2 point, Vector2 a, Vector2 b, Vector2 c) =>
    Cross(b - a, point - a) > 0 && Cross(c - b, point - b) > 0 && Cross(a - c, point - c) > 0;

  private static IEnumerable<int> Fan(List<int> ring)
  {
    for (int i = 1; i < ring.Count - 1; i++)
    {
      yield return ring[0];
      yield return ring[i];
      yield return ring[i + 1];
    }
  }

  private static double SignedArea(IReadOnlyList<Vector2> points, List<int> ring)
  {
    double twiceArea = 0;
    for (int i = 0; i < ring.Count; i++)
    {
      Vector2 current = points[ring[i]];
      Vector2 next = points[ring[(i + 1) % ring.Count]];
      twiceArea += Cross(current, next);
    }

    return twiceArea / 2;
  }

  private static double Cross(Vector2 left, Vector2 right) => (left.X * right.Y) - (left.Y * right.X);
}
