using Rhino.Geometry;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.Rhino.ToSpeckle.Raw;

public class NurbsCurveConverter : ITypedConverter<RG.NurbsCurve, SOG.Curve>
{
  private readonly ITypedConverter<RG.Polyline, SOG.Polyline> _polylineConverter;
  private readonly ITypedConverter<RG.Interval, SOP.Interval> _intervalConverter;
  private readonly ITypedConverter<RG.Box, SOG.Box> _boxConverter;
  private readonly IConverterSettingsStore<RhinoConversionSettings> _settingsStore;

  public NurbsCurveConverter(
    ITypedConverter<RG.Polyline, SOG.Polyline> polylineConverter,
    ITypedConverter<RG.Interval, SOP.Interval> intervalConverter,
    ITypedConverter<RG.Box, SOG.Box> boxConverter,
    IConverterSettingsStore<RhinoConversionSettings> settingsStore
  )
  {
    _polylineConverter = polylineConverter;
    _intervalConverter = intervalConverter;
    _boxConverter = boxConverter;
    _settingsStore = settingsStore;
  }

  /// <summary>
  /// Converts a NurbsCurve object to a Speckle Curve object.
  /// </summary>
  /// <param name="target">The NurbsCurve object to convert.</param>
  /// <returns>The converted Speckle Curve object.</returns>
  /// <remarks>
  /// ⚠️ This conversion does not respect Rhino knot vector structure.
  /// It adds 1 extra knot at the start and end of the vector by repeating the first and last value.
  /// This is because Rhino's standard of (controlPoints + degree + 1) wasn't followed on other software.
  /// </remarks>
  public SOG.Curve Convert(RG.NurbsCurve target)
  {
    // tolerance
    double tolerance = _settingsStore.Current.Document.ModelAbsoluteTolerance;

    if (target.ToPolyline(0, 1, 0, 0, 0, tolerance, 0, 0, true) is not PolylineCurve polylineCurve)
    {
      throw new ConversionException($"Failed to extract PolylineCurve from {target}");
    }

    if (!polylineCurve.TryGetPolyline(out Polyline? poly))
    {
      throw new ConversionException($"Failed to extract Polyline from {target}");
    }

    if (target.IsClosed)
    {
      poly.Add(poly[0]);
    }

    SOG.Polyline displayPoly = _polylineConverter.Convert(poly);

    // increase knot multiplicity to (# control points + degree + 1)
    // add extra knots at start & end  because Rhino's knot multiplicity standard is (# control points + degree - 1)
    var nurbsCurve = target.ToNurbsCurve();
    var knots = nurbsCurve.Knots.ToList();
    knots.Insert(0, knots[0]);
    knots.Insert(knots.Count - 1, knots[^1]);

    var myCurve = new SOG.Curve
    {
      displayValue = displayPoly,
      units = _settingsStore.Current.SpeckleUnits,
      weights = nurbsCurve.Points.Select(ctp => ctp.Weight).ToList(),
      points = nurbsCurve.Points.SelectMany(ctp => new[] { ctp.Location.X, ctp.Location.Y, ctp.Location.Z }).ToList(),
      knots = knots,
      degree = nurbsCurve.Degree,
      periodic = nurbsCurve.IsPeriodic,
      rational = nurbsCurve.IsRational,
      domain = _intervalConverter.Convert(nurbsCurve.Domain),
      closed = nurbsCurve.IsClosed,
      length = nurbsCurve.GetLength(),
      bbox = _boxConverter.Convert(new RG.Box(nurbsCurve.GetBoundingBox(true)))
    };

    return myCurve;
  }
}
