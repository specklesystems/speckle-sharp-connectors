using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.DatabaseServices;

namespace Speckle.Converters.Civil3dShared.Helpers;

/// <summary>World-space profile/section geometry, independent of view exaggeration and insertion point.</summary>
public static class CivilLinearGeometry
{
  public static Point3d[] ProfilePoints(Profile profile)
  {
    using var tr = profile.Database.TransactionManager.StartOpenCloseTransaction();
    var alignment = (Alignment)tr.GetObject(profile.AlignmentId, OpenMode.ForRead);
    var stations = new SortedSet<double> { profile.StartingStation, profile.EndingStation };
    foreach (ProfilePVI pvi in profile.PVIs)
    {
      if (pvi.RawStation >= profile.StartingStation && pvi.RawStation <= profile.EndingStation)
      {
        stations.Add(pvi.RawStation);
      }
    }
    foreach (ProfileEntity entity in profile.Entities)
    {
      stations.Add(entity.StartStation);
      stations.Add(entity.EndStation);
    }
    // Bound station spacing as well as chord error: a long S-curve can have its midpoint on the chord.
    double unitsPerMetre = UnitsConverter.GetConversionFactor(UnitsValue.Meters, profile.Database.Insunits);
    double tolerance = 0.001 * unitsPerMetre;
    double maxStep = 5 * unitsPerMetre;
    Point3d At(double station)
    {
      double x = 0, y = 0;
      alignment.PointLocation(station, profile.Offset, ref x, ref y);
      return new Point3d(x, y, profile.ElevationAt(station));
    }
    var result = new List<Point3d>();
    void AddSpan(double a, Point3d pa, double b, Point3d pb, int depth)
    {
      double mid = (a + b) / 2;
      var pm = At(mid);
      var chordMid = pa + (pb - pa) * 0.5;
      if (depth < 24 && (b - a > maxStep || pm.DistanceTo(chordMid) > tolerance))
      {
        AddSpan(a, pa, mid, pm, depth + 1);
        AddSpan(mid, pm, b, pb, depth + 1);
      }
      else
      {
        result.Add(pb);
      }
    }
    double[] ordered = stations.ToArray();
    if (ordered.Length == 0)
    {
      return Array.Empty<Point3d>();
    }
    result.Add(At(ordered[0]));
    for (int i = 1; i < ordered.Length; i++)
    {
      AddSpan(ordered[i - 1], result[^1], ordered[i], At(ordered[i]), 0);
    }
    return result.ToArray();
  }

  public static IEnumerable<Point3d[]> SectionSegments(Autodesk.Civil.DatabaseServices.Section section)
  {
    var points = section.SectionPoints.Cast<SectionPoint>().ToArray();
    var seen = new HashSet<(int, int)>();
    for (int i = 0; i < points.Length; i++)
    {
      int other = points[i].SegmentTo;
      // Never bridge a terrain hole or assume the collection is in polyline order.
      if (other < 0 || other >= points.Length || other == i || !seen.Add((Math.Min(i, other), Math.Max(i, other))))
      {
        continue;
      }
      yield return new[] { points[i].Location, points[other].Location };
    }
  }
}
