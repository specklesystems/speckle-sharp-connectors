using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Autocad.ToSpeckle.Raw;

public class NurbCurve3dToSpeckleConverter : ITypedConverter<AG.NurbCurve3d, SOG.Curve>
{
  private readonly ITypedConverter<AG.Interval, SOP.Interval> _intervalConverter;
  private readonly ITypedConverter<ADB.Polyline, SOG.Autocad.AutocadPolycurve> _polylineConverter;
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;

  public NurbCurve3dToSpeckleConverter(
    ITypedConverter<AG.Interval, SOP.Interval> intervalConverter,
    ITypedConverter<ADB.Polyline, SOG.Autocad.AutocadPolycurve> polylineConverter,
    IConverterSettingsStore<AutocadConversionSettings> settingsStore
  )
  {
    _intervalConverter = intervalConverter;
    _polylineConverter = polylineConverter;
    _settingsStore = settingsStore;
  }

  public SOG.Curve Convert(AG.NurbCurve3d target)
  {
    // The logic was taken from the Spline converter with adjusting properties and methods names

    // POC: HACK: check for incorrectly closed periodic curves (this seems like acad bug, has resulted from receiving rhino curves)
    bool periodicClosed = false;
    double length = 0;
    SOP.Interval domain = SOP.Interval.UnitInterval;

    length = target.GetLength(target.StartParameter, target.EndParameter, 0.001);
    domain = _intervalConverter.Convert(target.GetInterval());
    if (target.Knots.Count < target.NumberOfControlPoints + target.Degree + 1 && target.IsPeriodic(out _))
    {
      periodicClosed = true;
    }

    // get points
    List<AG.Point3d> points = new();
    for (int i = 0; i < target.NumberOfControlPoints; i++)
    {
      AG.Point3d controlPt = target.ControlPointAt(i);
      points.Add(controlPt);
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
    var knots = target.Knots.OfType<double>().ToList();
    if (periodicClosed)
    {
      double interval = knots[1] - knots[0]; //knot interval

      for (int i = 0; i < target.Degree; i++)
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
    for (int i = 0; i < target.NumWeights; i++)
    {
      double weight = target.GetWeightAt(i);
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
      periodic = target.IsPeriodic(out _),
      rational = target.IsRational,
      closed = periodicClosed || target.IsClosed(),
      length = length,
      domain = domain,
      bbox = null,
      units = _settingsStore.Current.SpeckleUnits,
      displayValue = GetDisplayValue(target)
    };

    return curve;
  }

  private SOG.Polyline GetDisplayValue(AG.NurbCurve3d nurb)
  {
    ADB.Polyline polyline = new() { Closed = nurb.IsClosed() };

    // Sample points along the curve
    int numPoints = 100;
    for (int i = 0; i <= numPoints; i++)
    {
      double param = nurb.StartParameter + (nurb.EndParameter - nurb.StartParameter) * (i / (double)numPoints);
      AG.Point3d pt = nurb.EvaluatePoint(param);
      polyline.AddVertexAt(i, new AG.Point2d(pt.X, pt.Y), 0, 0, 0);
    }

    SOG.Autocad.AutocadPolycurve polycurve = _polylineConverter.Convert(polyline);
    return new SOG.Polyline()
    {
      value = polycurve.value,
      length = polycurve.length,
      area = polycurve.area,
      closed = polycurve.closed,
      domain = polycurve.domain,
      units = polycurve.units
    };
  }
}
