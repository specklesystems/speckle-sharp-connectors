using Speckle.DoubleNumerics;

namespace Speckle.Common.SectionGeometry;

/// <summary>
/// Sweeps a <see cref="SectionProfile"/> along a straight path into a closed prism.
/// </summary>
/// <remarks>
/// This is the per-member hot path and is deliberately free of anything expensive - the profile arrives already
/// triangulated, so a sweep is two rings of point + u·widthAxis + v·depthAxis plus index bookkeeping.
/// NOTE: vertices are shared between walls and caps. A 12-point I-section is 24 vertices, not the ~132 an unshared
/// extrusion emits. That difference is the whole payload budget of the feature, so keep it.
/// NOTE: axes are assumed orthonormal. A host transformation matrix already is; <see cref="MemberAxes"/> guarantees
/// it for hosts that only report a rotation angle.
/// </remarks>
public static class ProfileSweeper
{
  /// <summary>Sweeps a profile centred on the path.</summary>
  public static ProfileMesh Sweep(
    SectionProfile profile,
    Vector3 startPoint,
    Vector3 endPoint,
    Vector3 widthAxis,
    Vector3 depthAxis
  ) => Sweep(profile, startPoint, endPoint, widthAxis, depthAxis, default);

  /// <summary>Sweeps a profile offset within its own plane, which is how insertion points get applied.</summary>
  /// <param name="profileOffset">
  /// Translation in profile space. Shifting by the negated cardinal point puts that point on the path - for a beam
  /// inserted at top centre, that's (0, -Bounds.MaxV).
  /// </param>
  public static ProfileMesh Sweep(
    SectionProfile profile,
    Vector3 startPoint,
    Vector3 endPoint,
    Vector3 widthAxis,
    Vector3 depthAxis,
    Vector2 profileOffset
  )
  {
    if (profile is null)
    {
      throw new ArgumentNullException(nameof(profile));
    }

    IReadOnlyList<Vector2> points = profile.Points;
    int pointCount = points.Count;

    var vertices = new List<double>(pointCount * 6);
    AppendRing(vertices, points, startPoint, widthAxis, depthAxis, profileOffset);
    AppendRing(vertices, points, endPoint, widthAxis, depthAxis, profileOffset);

    // cap triangles face along widthAxis × depthAxis, so whichever ring sits further that way keeps their winding
    // and the other is reversed. walls follow suit. NOTE: with CSi's right-handed local axes the cross product runs
    // back along the member, so this branch is the normal case rather than the exception.
    bool startsBehindEnd = Vector3.Dot(endPoint - startPoint, Vector3.Cross(widthAxis, depthAxis)) >= 0;

    var faces = new List<int>((pointCount * 5) + (profile.CapTriangles.Count / 3 * 8));
    AppendWalls(faces, profile, pointCount, startsBehindEnd);
    AppendCaps(faces, profile.CapTriangles, pointCount, startsBehindEnd);

    return new ProfileMesh(vertices, faces);
  }

  private static void AppendRing(
    List<double> vertices,
    IReadOnlyList<Vector2> points,
    Vector3 origin,
    Vector3 widthAxis,
    Vector3 depthAxis,
    Vector2 offset
  )
  {
    for (int i = 0; i < points.Count; i++)
    {
      double u = points[i].X + offset.X;
      double v = points[i].Y + offset.Y;

      vertices.Add(origin.X + (u * widthAxis.X) + (v * depthAxis.X));
      vertices.Add(origin.Y + (u * widthAxis.Y) + (v * depthAxis.Y));
      vertices.Add(origin.Z + (u * widthAxis.Z) + (v * depthAxis.Z));
    }
  }

  private static void AppendWalls(List<int> faces, SectionProfile profile, int pointCount, bool startsBehindEnd)
  {
    foreach (ProfileLoop loop in profile.Loops)
    {
      for (int step = 0; step < loop.Count; step++)
      {
        // holes are stored counter-clockwise like everything else, so walking them backwards is what turns their
        // walls to face into the void rather than out of it
        int here = loop.Start + (loop.IsHole ? loop.Count - 1 - step : step);
        int there = loop.Start + (loop.IsHole ? Wrap(loop.Count - 2 - step, loop.Count) : Wrap(step + 1, loop.Count));

        faces.Add(4);
        if (startsBehindEnd)
        {
          faces.Add(here);
          faces.Add(there);
          faces.Add(pointCount + there);
          faces.Add(pointCount + here);
        }
        else
        {
          faces.Add(pointCount + here);
          faces.Add(pointCount + there);
          faces.Add(there);
          faces.Add(here);
        }
      }
    }
  }

  private static void AppendCaps(
    List<int> faces,
    IReadOnlyList<int> capTriangles,
    int pointCount,
    bool startsBehindEnd
  )
  {
    int forwardRing = startsBehindEnd ? pointCount : 0;
    int reversedRing = startsBehindEnd ? 0 : pointCount;

    for (int i = 0; i + 2 < capTriangles.Count; i += 3)
    {
      faces.Add(3);
      faces.Add(forwardRing + capTriangles[i]);
      faces.Add(forwardRing + capTriangles[i + 1]);
      faces.Add(forwardRing + capTriangles[i + 2]);

      faces.Add(3);
      faces.Add(reversedRing + capTriangles[i + 2]);
      faces.Add(reversedRing + capTriangles[i + 1]);
      faces.Add(reversedRing + capTriangles[i]);
    }
  }

  private static int Wrap(int index, int count) => ((index % count) + count) % count;
}
