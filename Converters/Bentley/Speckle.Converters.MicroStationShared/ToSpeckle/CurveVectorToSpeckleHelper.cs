using Bentley.GeometryNET;
using Speckle.Objects.Geometry;
using Speckle.Sdk.Models;

namespace Speckle.Converters.MicroStation.ToSpeckle;

/// <summary>
/// Translates a Bentley.GeometryNET <see cref="CurveVector"/> (the universal curve container
/// returned by every line/arc/shape/complex/bspline managed element via
/// <c>ChainHeaderElement.GetCurveVector()</c> et al.) into a Speckle <see cref="Polyline"/>.
/// <para>
/// Walks every primitive in the vector — Line / Arc / LineString / BSplineCurve / Spiral / etc. —
/// and accumulates a flat point list. Arcs / splines / spirals are stroked into segments via
/// the primitive's proxy MSBsplineCurve at a chord tolerance of <see cref="STROKE_TOLERANCE"/>.
/// Adjacent primitives that share an endpoint have the duplicate vertex dropped.
/// </para>
/// <para>
/// Stronger type fidelity (preserve Arc / Ellipse / Curve as their typed Speckle equivalents
/// instead of always stroking to Polyline) is a follow-up. The entry-point shape stays stable
/// so adding cases here transparently improves every consumer converter.
/// </para>
/// </summary>
internal static class CurveVectorToSpeckleHelper
{
  // Chord tolerance for stroking arcs / b-splines / spirals into polyline segments. 0.001 in
  // master units is sub-millimetre at typical drawing scales — fine visually without exploding
  // the vertex count.
  private const double STROKE_TOLERANCE = 0.001;

  public static Base ToSpeckle(CurveVector cv, string units, string applicationId)
  {
    var boundary = cv.GetBoundaryType();
    bool isClosed = boundary == CurveVector.BoundaryType.Outer || boundary == CurveVector.BoundaryType.Inner;

    var points = new List<DPoint3d>();
    bool firstPrimitive = true;

    for (int i = 0; ; i++)
    {
      var prim = cv.GetPrimitive(i);
      if (prim == null)
      {
        break;
      }

      var before = points.Count;
      AppendPrimitivePoints(prim, points);

      // For chained primitives, drop the duplicate join vertex (current primitive's start ==
      // previous primitive's end).
      if (!firstPrimitive && points.Count > before && before > 0)
      {
        var prevTail = points[before - 1];
        var newHead = points[before];
        if (PointsEqual(prevTail, newHead))
        {
          points.RemoveAt(before);
        }
      }

      firstPrimitive = false;
    }

    // If marked closed and first/last coincide, drop the duplicate — Polyline.closed carries it.
    if (isClosed && points.Count >= 2 && PointsEqual(points[0], points[^1]))
    {
      points.RemoveAt(points.Count - 1);
    }

    var values = new List<double>(points.Count * 3);
    foreach (var p in points)
    {
      values.Add(p.X);
      values.Add(p.Y);
      values.Add(p.Z);
    }

    return new Polyline
    {
      value = values,
      closed = isClosed,
      units = units,
      applicationId = applicationId,
    };
  }

  /// <summary>
  /// Stroke a single primitive into <paramref name="points"/>. The primitive's start point is
  /// always included; arcs / splines / spirals are stroked via their proxy MSBsplineCurve.
  /// </summary>
  private static void AppendPrimitivePoints(CurvePrimitive prim, List<DPoint3d> points)
  {
    switch (prim.GetCurvePrimitiveType())
    {
      case CurvePrimitive.CurvePrimitiveType.Line:
      {
        if (prim.TryGetLine(out DSegment3d seg))
        {
          points.Add(seg.StartPoint);
          points.Add(seg.EndPoint);
        }
        break;
      }

      case CurvePrimitive.CurvePrimitiveType.LineString:
      case CurvePrimitive.CurvePrimitiveType.PointString:
      {
        var ls = new List<DPoint3d>();
        if (prim.TryGetLineString(ls))
        {
          points.AddRange(ls);
        }
        break;
      }

      case CurvePrimitive.CurvePrimitiveType.Arc:
      case CurvePrimitive.CurvePrimitiveType.BsplineCurve:
      case CurvePrimitive.CurvePrimitiveType.InterpolationCurve:
      case CurvePrimitive.CurvePrimitiveType.Spiral:
      case CurvePrimitive.CurvePrimitiveType.AkimaCurve:
      {
        // Stroke via MSBsplineCurve — every primitive type has a proxy bspline representation.
        var bspline = prim.GetProxyBsplineCurve() ?? prim.GetBsplineCurve();
        if (bspline == null)
        {
          break;
        }
        var strokes = new List<DPoint3d>();
        bspline.AddStrokes(strokes, null, null, STROKE_TOLERANCE, 0.0, 0.0, false);
        points.AddRange(strokes);
        break;
      }

      default:
      {
        // Unknown / nested CurveVector primitive — walk recursively.
        var child = prim.GetChildCurveVector();
        if (child != null)
        {
          for (int i = 0; ; i++)
          {
            var childPrim = child.GetPrimitive(i);
            if (childPrim == null)
            {
              break;
            }
            AppendPrimitivePoints(childPrim, points);
          }
        }
        break;
      }
    }
  }

  private static bool PointsEqual(DPoint3d a, DPoint3d b) =>
    Math.Abs(a.X - b.X) < 1e-9 && Math.Abs(a.Y - b.Y) < 1e-9 && Math.Abs(a.Z - b.Z) < 1e-9;
}
