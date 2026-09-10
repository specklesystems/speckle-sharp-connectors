using Speckle.Converters.MicroStation.Services;
using Speckle.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.MicroStation.ToSpeckle.Raw;

/// <summary>
/// The typed curve suite — the managed-API port of dgnextract's <c>curves_dgn.h</c> extraction
/// strategy chain. <see cref="DPN.Elements.CurvePathQuery.ElementToCurveVector"/> hands every DGN
/// curve-bearing element (line, linestring, shape, arc, ellipse, bspline, point/complex string,
/// complex shape, curve, multiline) over as a <see cref="BG.CurveVector"/> whose primitives keep
/// their native types, so one converter pair covers the whole suite:
/// <list type="bullet">
/// <item>Line → <see cref="SOG.Line"/> (zero-length kept, matching dgnextract's LineExtractor)</item>
/// <item>LineString → <see cref="SOG.Polyline"/></item>
/// <item>Arc (circular, partial) → <see cref="SOG.Arc"/>; full circular → <see cref="SOG.Circle"/>;
/// elliptic → <see cref="SOG.Ellipse"/> — classified on the ambient-transformed axes, so an
/// affine placement that turns a circle into an ellipse is represented exactly</item>
/// <item>BsplineCurve → <see cref="SOG.Curve"/> (full NURBS: poles/knots/weights)</item>
/// <item>InterpolationCurve / Spiral / Akima / anything else → proxy bspline → <see cref="SOG.Curve"/>,
/// or a sampled <see cref="SOG.Polyline"/> — no curve is ever dropped (GenericCurveExtractor)</item>
/// <item>PointString → <see cref="SOG.Point"/> per vertex</item>
/// </list>
/// </summary>
public class CurvePrimitiveConverter(GeometryMapper mapper)
{
  // Chord tolerance for stroking proxy bsplines, in master units (dgnextract used per-element
  // bbox-derived tolerances only for meshes; curve sampling used parameter space). Sub-millimetre
  // at typical scales without exploding vertex counts.
  private const double STROKE_TOLERANCE_MASTER_UNITS = 0.001;
  private const int SAMPLE_COUNT_FALLBACK = 64;

  public void Convert(BG.CurvePrimitive primitive, List<Base> output)
  {
    switch (primitive.GetCurvePrimitiveType())
    {
      case BG.CurvePrimitive.CurvePrimitiveType.Line:
      {
        if (primitive.TryGetLine(out BG.DSegment3d segment))
        {
          output.Add(ConvertLine(segment));
        }
        break;
      }
      case BG.CurvePrimitive.CurvePrimitiveType.LineString:
      {
        var points = new List<BG.DPoint3d>();
        if (primitive.TryGetLineString(points) && points.Count >= 2)
        {
          output.Add(ConvertPolyline(points, closed: false));
        }
        break;
      }
      case BG.CurvePrimitive.CurvePrimitiveType.Arc:
      {
        if (primitive.TryGetArc(out BG.DEllipse3d arc))
        {
          output.Add(ConvertEllipticArc(arc));
        }
        break;
      }
      case BG.CurvePrimitive.CurvePrimitiveType.BsplineCurve:
      {
        BG.MSBsplineCurve? bspline = primitive.GetBsplineCurve();
        if (bspline != null)
        {
          output.Add(ConvertBspline(bspline));
        }
        break;
      }
      case BG.CurvePrimitive.CurvePrimitiveType.PointString:
      {
        var points = new List<BG.DPoint3d>();
        if (primitive.TryGetLineString(points))
        {
          foreach (BG.DPoint3d p in points)
          {
            output.Add(mapper.MapPoint(p));
          }
        }
        break;
      }
      case BG.CurvePrimitive.CurvePrimitiveType.CurveVector:
      {
        // Nested vector (complex chains produce these) — recurse.
        BG.CurveVector? child = primitive.GetChildCurveVector();
        if (child != null)
        {
          new CurveVectorConverter(mapper, this).Convert(child, output);
        }
        break;
      }
      default:
      {
        // InterpolationCurve, Spiral, AkimaCurve, PartialCurve, NotClassified — proxy bspline
        // keeps exact NURBS geometry; parameter sampling is the last resort. Never drop.
        BG.MSBsplineCurve? proxy = primitive.GetProxyBsplineCurve() ?? primitive.GetBsplineCurve();
        if (proxy != null)
        {
          output.Add(ConvertBspline(proxy));
        }
        else
        {
          SOG.Polyline? sampled = SamplePrimitive(primitive);
          if (sampled != null)
          {
            output.Add(sampled);
          }
        }
        break;
      }
    }
  }

  public SOG.Line ConvertLine(BG.DSegment3d segment) =>
    new()
    {
      start = mapper.MapPoint(segment.StartPoint),
      end = mapper.MapPoint(segment.EndPoint),
      units = mapper.Units,
    };

  public SOG.Polyline ConvertPolyline(IReadOnlyList<BG.DPoint3d> points, bool closed)
  {
    var value = new List<double>(points.Count * 3);
    int count = points.Count;
    // Closed polylines carry closure in the flag, not a duplicated vertex (ShapeExtractor).
    if (closed && count >= 2 && PointsCoincide(points[0], points[count - 1]))
    {
      count--;
    }
    for (int i = 0; i < count; i++)
    {
      var (x, y, z) = mapper.MapXyz(points[i]);
      value.Add(x);
      value.Add(y);
      value.Add(z);
    }
    return new SOG.Polyline
    {
      value = value,
      closed = closed,
      units = mapper.Units,
    };
  }

  /// <summary>
  /// DEllipse3d → Arc / Circle / Ellipse. The classification happens AFTER the ambient transform:
  /// axes are mapped through the ambient linear part (+UOR scale), so placement-induced distortion
  /// is represented exactly (an affine image of an ellipse is an ellipse).
  /// </summary>
  public Base ConvertEllipticArc(BG.DEllipse3d arc)
  {
    BG.DVector3d v0 = mapper.MapVectorRaw(arc.Vector0);
    BG.DVector3d v90 = mapper.MapVectorRaw(arc.Vector90);
    double r0 = v0.Magnitude;
    double r90 = v90.Magnitude;
    double dot = v0.X * v90.X + v0.Y * v90.Y + v0.Z * v90.Z;
    bool circular =
      r0 > 0 && r90 > 0 && Math.Abs(r0 - r90) <= 1e-9 * Math.Max(r0, r90) && Math.Abs(dot) <= 1e-9 * r0 * r90;

    double startRadians = arc.StartAngle.Radians;
    double sweepRadians = arc.SweepAngle.Radians;
    bool fullSweep = Math.Abs(Math.Abs(sweepRadians) - 2 * Math.PI) < 1e-9 || arc.IsFull();

    SOG.Point center = mapper.MapPoint(arc.Center);
    SOG.Plane plane = PlaneFromAxes(center, v0, v90, r0, r90);

    if (circular && fullSweep)
    {
      return new SOG.Circle
      {
        plane = plane,
        radius = r0,
        units = mapper.Units,
      };
    }

    if (circular)
    {
      SOG.Point start = mapper.MapPoint(PointAt(arc, startRadians));
      SOG.Point mid = mapper.MapPoint(PointAt(arc, startRadians + sweepRadians / 2.0));
      SOG.Point end = mapper.MapPoint(PointAt(arc, startRadians + sweepRadians));
      return new SOG.Arc
      {
        plane = plane,
        startPoint = start,
        midPoint = mid,
        endPoint = end,
        domain = new SOP.Interval { start = startRadians, end = startRadians + sweepRadians },
        units = mapper.Units,
      };
    }

    // Elliptic: normalize to major/minor axes so plane.xdir is the major axis (GeEllipArcConverter).
    return ConvertEllipse(arc, v0, v90, r0, r90, startRadians, sweepRadians, fullSweep, center);
  }

  private SOG.Ellipse ConvertEllipse(
    BG.DEllipse3d arc,
    BG.DVector3d v0,
    BG.DVector3d v90,
    double r0,
    double r90,
    double startRadians,
    double sweepRadians,
    bool fullSweep,
    SOG.Point center
  )
  {
    // Build the mapped-axes ellipse and let Bentley's own major/minor decomposition normalize the
    // (possibly non-perpendicular) axis pair into a proper major/minor frame.
    var mapped = new BG.DEllipse3d
    {
      Center = default,
      Vector0 = v0,
      Vector90 = v90,
      StartAngle = arc.StartAngle,
      SweepAngle = arc.SweepAngle,
    };

    double major = Math.Max(r0, r90);
    double minor = Math.Min(r0, r90);
    BG.DVector3d xAxis = r0 >= r90 ? v0 : v90;
    BG.DVector3d yAxis = r0 >= r90 ? v90 : v0;
    double trimStart = startRadians;
    double trimEnd = startRadians + sweepRadians;

    if (
      mapped.GetMajorMinorData(
        out BG.DPoint3d _,
        out BG.DMatrix3d frame,
        out double majorLen,
        out double minorLen,
        out BG.Angle startAngle,
        out BG.Angle sweepAngle
      )
    )
    {
      major = majorLen;
      minor = minorLen;
      xAxis = frame.Multiply(new BG.DVector3d { X = 1 });
      yAxis = frame.Multiply(new BG.DVector3d { Y = 1 });
      trimStart = startAngle.Radians;
      trimEnd = startAngle.Radians + sweepAngle.Radians;
    }

    SOG.Plane plane = new()
    {
      origin = center,
      normal = Normalized(Cross(xAxis, yAxis)),
      xdir = Normalized(xAxis),
      ydir = Normalized(yAxis),
      units = mapper.Units,
    };

    return new SOG.Ellipse
    {
      plane = plane,
      firstRadius = major,
      secondRadius = minor,
      domain = new SOP.Interval { start = 0, end = 2 * Math.PI },
      trimDomain = fullSweep ? null : new SOP.Interval { start = trimStart, end = trimEnd },
      units = mapper.Units,
    };
  }

  public SOG.Curve ConvertBspline(BG.MSBsplineCurve bspline)
  {
    int degree = Math.Max(1, bspline.Order - 1);

    var points = new List<double>(bspline.PoleCount * 3);
    foreach (BG.DPoint3d pole in bspline.Poles)
    {
      var (x, y, z) = mapper.MapXyz(pole);
      points.Add(x);
      points.Add(y);
      points.Add(z);
    }

    List<double> knots = bspline.Knots is { } k ? [.. k] : [];
    List<double> weights;
    if (bspline.IsRational && bspline.Weights is { } w)
    {
      weights = [.. w];
    }
    else
    {
      weights = [.. Enumerable.Repeat(1.0, bspline.PoleCount)];
    }

    // displayValue polyline via chord stroking; the tolerance is master units → UORs.
    var strokes = new List<BG.DPoint3d>();
    bspline.AddStrokes(strokes, null, null, ChordToleranceUor(), 0.0, 0.0, true);
    SOG.Polyline display = ConvertPolyline(strokes, closed: false);
    double length = PolylineLength(display);

    double domainEnd = knots.Count > 0 ? knots[^1] : 1.0;
    double domainStart = knots.Count > 0 ? knots[0] : 0.0;

    return new SOG.Curve
    {
      points = points,
      knots = knots,
      weights = weights,
      degree = degree,
      periodic = false,
      rational = bspline.IsRational,
      closed = bspline.IsClosed,
      length = length,
      domain = new SOP.Interval { start = domainStart, end = domainEnd },
      displayValue = display,
      units = mapper.Units,
    };
  }

  private SOG.Polyline? SamplePrimitive(BG.CurvePrimitive primitive)
  {
    var points = new List<BG.DPoint3d>(SAMPLE_COUNT_FALLBACK + 1);
    for (int i = 0; i <= SAMPLE_COUNT_FALLBACK; i++)
    {
      if (primitive.FractionToPoint((double)i / SAMPLE_COUNT_FALLBACK, out BG.DPoint3d p))
      {
        points.Add(p);
      }
    }
    return points.Count >= 2 ? ConvertPolyline(points, closed: false) : null;
  }

  // STROKE_TOLERANCE is expressed in master units; stroking happens in UOR space.
  private double ChordToleranceUor() => STROKE_TOLERANCE_MASTER_UNITS * mapper.UorPerMasterForTolerances();

  private static double PolylineLength(SOG.Polyline polyline)
  {
    List<double> v = polyline.value;
    double total = 0;
    for (int i = 3; i + 2 < v.Count; i += 3)
    {
      double dx = v[i] - v[i - 3];
      double dy = v[i + 1] - v[i - 2];
      double dz = v[i + 2] - v[i - 1];
      total += Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
    return total;
  }

  private static BG.DPoint3d PointAt(BG.DEllipse3d e, double radians) =>
    new()
    {
      X = e.Center.X + e.Vector0.X * Math.Cos(radians) + e.Vector90.X * Math.Sin(radians),
      Y = e.Center.Y + e.Vector0.Y * Math.Cos(radians) + e.Vector90.Y * Math.Sin(radians),
      Z = e.Center.Z + e.Vector0.Z * Math.Cos(radians) + e.Vector90.Z * Math.Sin(radians),
    };

  private SOG.Plane PlaneFromAxes(SOG.Point origin, BG.DVector3d v0, BG.DVector3d v90, double r0, double r90)
  {
    BG.DVector3d x = r0 > 0 ? v0 : new BG.DVector3d { X = 1 };
    BG.DVector3d y = r90 > 0 ? v90 : new BG.DVector3d { Y = 1 };
    return new SOG.Plane
    {
      origin = origin,
      normal = Normalized(Cross(x, y)),
      xdir = Normalized(x),
      ydir = Normalized(y),
      units = mapper.Units,
    };
  }

  private SOG.Vector Normalized(BG.DVector3d v)
  {
    double len = v.Magnitude;
    if (len <= 0)
    {
      return new SOG.Vector
      {
        x = 0,
        y = 0,
        z = 1,
        units = mapper.Units,
      };
    }
    return new SOG.Vector
    {
      x = v.X / len,
      y = v.Y / len,
      z = v.Z / len,
      units = mapper.Units,
    };
  }

  private static BG.DVector3d Cross(BG.DVector3d a, BG.DVector3d b) =>
    new()
    {
      X = a.Y * b.Z - a.Z * b.Y,
      Y = a.Z * b.X - a.X * b.Z,
      Z = a.X * b.Y - a.Y * b.X,
    };

  private static bool PointsCoincide(BG.DPoint3d a, BG.DPoint3d b) =>
    Math.Abs(a.X - b.X) < 1e-9 && Math.Abs(a.Y - b.Y) < 1e-9 && Math.Abs(a.Z - b.Z) < 1e-9;
}

/// <summary>
/// CurveVector → Speckle, honoring the boundary type (port of the managed extractor's
/// ComplexCurveConverter + ShapeExtractor policies):
/// <list type="bullet">
/// <item><c>Open</c> single primitive → that primitive's typed conversion</item>
/// <item><c>Open</c> chain → <see cref="SOG.Polycurve"/> of typed segments</item>
/// <item><c>Outer</c>/<c>Inner</c> all-linear → one closed <see cref="SOG.Polyline"/> (dup closing
/// vertex dropped, &lt; 3 distinct vertices skipped); mixed → closed <see cref="SOG.Polycurve"/></item>
/// <item><c>ParityRegion</c>/<c>UnionRegion</c>/<c>None</c> → children converted independently</item>
/// </list>
/// </summary>
public class CurveVectorConverter(GeometryMapper mapper, CurvePrimitiveConverter primitiveConverter)
{
  public void Convert(BG.CurveVector vector, List<Base> output)
  {
    BG.CurveVector.BoundaryType boundary = vector.GetBoundaryType();
    switch (boundary)
    {
      case BG.CurveVector.BoundaryType.Outer:
      case BG.CurveVector.BoundaryType.Inner:
        ConvertClosed(vector, output);
        return;
      case BG.CurveVector.BoundaryType.ParityRegion:
      case BG.CurveVector.BoundaryType.UnionRegion:
      case BG.CurveVector.BoundaryType.None:
      case BG.CurveVector.BoundaryType.Open:
      default:
        break;
    }

    var pieces = new List<Base>();
    for (int i = 0; ; i++)
    {
      BG.CurvePrimitive? primitive = vector.GetPrimitive(i);
      if (primitive == null)
      {
        break;
      }
      primitiveConverter.Convert(primitive, pieces);
    }

    if (boundary == BG.CurveVector.BoundaryType.Open && pieces.Count > 1 && pieces.All(p => p is ICurve))
    {
      output.Add(
        new SOG.Polycurve
        {
          segments = [.. pieces.Cast<ICurve>()],
          closed = false,
          units = mapper.Units,
        }
      );
      return;
    }

    output.AddRange(pieces);
  }

  private void ConvertClosed(BG.CurveVector vector, List<Base> output)
  {
    // All-linear closed region → one closed Polyline with merged vertices.
    if (TryCollectLinearLoop(vector, out List<BG.DPoint3d> loop))
    {
      if (DistinctCount(loop) < 3)
      {
        return; // degenerate shape (ShapeExtractor skip rule)
      }
      output.Add(primitiveConverter.ConvertPolyline(loop, closed: true));
      return;
    }

    var pieces = new List<Base>();
    for (int i = 0; ; i++)
    {
      BG.CurvePrimitive? primitive = vector.GetPrimitive(i);
      if (primitive == null)
      {
        break;
      }
      primitiveConverter.Convert(primitive, pieces);
    }

    if (pieces.Count == 1 && pieces[0] is SOG.Circle or SOG.Ellipse)
    {
      // A closed region that IS a full circle/ellipse — the typed object already closes itself.
      output.AddRange(pieces);
      return;
    }

    if (pieces.Count >= 1 && pieces.All(p => p is ICurve))
    {
      output.Add(
        new SOG.Polycurve
        {
          segments = [.. pieces.Cast<ICurve>()],
          closed = true,
          units = mapper.Units,
        }
      );
      return;
    }

    output.AddRange(pieces);
  }

  private static bool TryCollectLinearLoop(BG.CurveVector vector, out List<BG.DPoint3d> loop)
  {
    loop = [];
    for (int i = 0; ; i++)
    {
      BG.CurvePrimitive? primitive = vector.GetPrimitive(i);
      if (primitive == null)
      {
        break;
      }
      BG.CurvePrimitive.CurvePrimitiveType type = primitive.GetCurvePrimitiveType();
      if (type == BG.CurvePrimitive.CurvePrimitiveType.Line)
      {
        if (!primitive.TryGetLine(out BG.DSegment3d segment))
        {
          return false;
        }
        AppendMerged(loop, segment.StartPoint);
        AppendMerged(loop, segment.EndPoint);
      }
      else if (type == BG.CurvePrimitive.CurvePrimitiveType.LineString)
      {
        var points = new List<BG.DPoint3d>();
        if (!primitive.TryGetLineString(points))
        {
          return false;
        }
        foreach (BG.DPoint3d p in points)
        {
          AppendMerged(loop, p);
        }
      }
      else
      {
        return false;
      }
    }
    return loop.Count >= 2;
  }

  private static void AppendMerged(List<BG.DPoint3d> loop, BG.DPoint3d p)
  {
    if (loop.Count > 0)
    {
      BG.DPoint3d last = loop[^1];
      if (Math.Abs(last.X - p.X) < 1e-9 && Math.Abs(last.Y - p.Y) < 1e-9 && Math.Abs(last.Z - p.Z) < 1e-9)
      {
        return;
      }
    }
    loop.Add(p);
  }

  private static int DistinctCount(List<BG.DPoint3d> points)
  {
    int count = points.Count;
    if (
      count >= 2
      && Math.Abs(points[0].X - points[^1].X) < 1e-9
      && Math.Abs(points[0].Y - points[^1].Y) < 1e-9
      && Math.Abs(points[0].Z - points[^1].Z) < 1e-9
    )
    {
      count--;
    }
    return count;
  }
}
