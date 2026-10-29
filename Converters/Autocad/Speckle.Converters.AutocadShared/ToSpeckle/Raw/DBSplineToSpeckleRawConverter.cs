using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Autocad.ToSpeckle.Raw;

public class DBSplineToSpeckleRawConverter : ITypedConverter<ADB.Spline, SOG.Curve>
{
  private readonly ITypedConverter<ADB.Spline, SOG.Polyline> _displayValueConverter;
  private readonly ITypedConverter<AG.Interval, SOP.Interval> _intervalConverter;
  private readonly IReferencePointConverter _referencePointConverter;
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;

  public DBSplineToSpeckleRawConverter(
    ITypedConverter<ADB.Spline, SOG.Polyline> displayValueConverter,
    ITypedConverter<AG.Interval, SOP.Interval> intervalConverter,
    IReferencePointConverter referencePointConverter,
    IConverterSettingsStore<AutocadConversionSettings> settingsStore
  )
  {
    _displayValueConverter = displayValueConverter;
    _intervalConverter = intervalConverter;
    _referencePointConverter = referencePointConverter;
    _settingsStore = settingsStore;
  }

  public Base Convert(object target) => Convert((ADB.Spline)target);

  public SOG.Curve Convert(ADB.Spline target)
  {
    // get nurbs and geo data
    ADB.NurbsData data = target.NurbsData;

    // POC: HACK: check for incorrectly closed periodic curves (this seems like acad bug, has resulted from receiving rhino curves)
    bool periodicClosed = false;
    double length = 0;
    SOP.Interval domain = SOP.Interval.UnitInterval;
    using AG.Curve3d curve3d = target.GetGeCurve();
    if (curve3d is AG.NurbCurve3d nurbs)
    {
      length = nurbs.GetLength(nurbs.StartParameter, nurbs.EndParameter, 0.001);
      domain = _intervalConverter.Convert(nurbs.GetInterval());
      if (nurbs.Knots.Count < nurbs.NumberOfControlPoints + nurbs.Degree + 1 && target.IsPeriodic)
      {
        periodicClosed = true;
      }
    }

    // get points, transformed by reference point setting
    List<AG.Point3d> points = new();
    foreach (AG.Point3d point in data.GetControlPoints().OfType<AG.Point3d>())
    {
      points.Add(_referencePointConverter.ConvertWCSPointToExternalCoordinates(point));
    }

    // NOTE: for closed periodic splines, autocad does not track last #degree points.
    // Add the first #degree control points to the list if so.
    if (periodicClosed)
    {
      points.AddRange(points.GetRange(0, target.Degree));
    }

    // get knots
    // NOTE: for closed periodic splines, autocad has #control points + 1 knots.
    // Add #degree extra knots to beginning and end with #degree - 1 multiplicity for first and last
    var knots = data.GetKnots().OfType<double>().ToList();
    if (periodicClosed)
    {
      double interval = knots[1] - knots[0]; //knot interval

      for (int i = 0; i < data.Degree; i++)
      {
        if (i < 2)
        {
          knots.Insert(knots.Count, knots[^1] + interval);
          knots.Insert(0, knots[0] - interval);
        }
        else
        {
          knots.Insert(knots.Count, knots[^1]);
          knots.Insert(0, knots[0]);
        }
      }
    }

    // get weights
    // NOTE: autocad assigns unweighted points a value of -1, and will return an empty list in the spline's nurbsdata if no points are weighted
    // NOTE: for closed periodic splines, autocad does not track last #degree points. Add the first #degree weights to the list if so.
    List<double> weights = new();
    for (int i = 0; i < target.NumControlPoints; i++)
    {
      double weight = target.WeightAt(i);
      weights.Add(weight <= 0 ? 1 : weight);
    }

    if (periodicClosed)
    {
      weights.AddRange(weights.GetRange(0, target.Degree));
    }

    // set nurbs curve info
    var curve = new SOG.Curve
    {
      points = points.SelectMany(o => o.ToArray()).ToList(),
      knots = knots,
      weights = weights,
      degree = target.Degree,
      periodic = target.IsPeriodic,
      rational = target.IsRational,
      closed = periodicClosed || target.Closed,
      length = length,
      domain = domain,
      units = _settingsStore.Current.SpeckleUnits,
      // POC: get display value if this is a database-resident spline
      // POC: if this is called by another converter that has created a spline, assumes the display value is set by that converter
      displayValue = target.Database is not null ? _displayValueConverter.Convert(target) : null!,
    };

    return curve;
  }
}
