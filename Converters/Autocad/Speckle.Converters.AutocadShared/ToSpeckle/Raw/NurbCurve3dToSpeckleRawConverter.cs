using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.Autocad.ToSpeckle.Raw;

public class NurbCurve3dToSpeckleConverter : ITypedConverter<AG.NurbCurve3d, SOG.Curve>
{
  private readonly ITypedConverter<AG.Point3d, SOG.Point> _pointConverter;
  private readonly ITypedConverter<AG.Plane, SOG.Plane> _planeConverter;
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;

  public NurbCurve3dToSpeckleConverter(
    ITypedConverter<AG.Point3d, SOG.Point> pointConverter,
    ITypedConverter<AG.Plane, SOG.Plane> planeConverter,
    IConverterSettingsStore<AutocadConversionSettings> settingsStore
  )
  {
    _pointConverter = pointConverter;
    _planeConverter = planeConverter;
    _settingsStore = settingsStore;
  }

  public SOG.Curve Convert(AG.NurbCurve3d target)
  {
    SOG.Plane plane = _planeConverter.Convert(new(target.Center, target.Normal));
    SOG.Point start = _pointConverter.Convert(target.StartPoint);
    SOG.Point end = _pointConverter.Convert(target.EndPoint);
    double startParam = target.GetParameterOf(target.StartPoint);
    double endParam = target.GetParameterOf(target.EndPoint);
    AG.Point3d midPoint = target.EvaluatePoint(target.StartAngle + (target.EndAngle - target.StartAngle) / 2);
    SOG.Point mid = _pointConverter.Convert(midPoint);

    // tolerance: don't use AG.Tolerance.Global.EqualPoint, as it is usually 1E-5
    double tolerance = target.FitTolerance.EqualPoint;
    if (target.())
    {
      throw new ConversionException($"Failed to extract PolylineCurve from {target}");
    }

    if (!polylineCurve.TryGetPolyline(out SOG.Polyline? poly))
    {
      throw new ConversionException($"Failed to extract Polyline from {target}");
    }

    if (target.IsClosed())
    {
      poly.Add(poly[0]);
    }

    SOG.Polyline displayPoly = _polylineConverter.Convert(poly);

    // increase knot multiplicity to (# control points + degree + 1)
    var knots = new List<double>();
    for (int i = 0; i < target.Knots.Count; i++)
    {
      knots.Add(target.KnotAt(i));
    }
    
    var curve = new SOG.Curve
    {
      displayValue = displayPoly,
      units = _settingsStore.Current.SpeckleUnits,
      weights = nurbsCurve.Points.Select(ctp => ctp.Weight).ToList(),
      points = target.ControlPointAt().SelectMany(ctp => new[] { ctp.Location.X, ctp.Location.Y, ctp.Location.Z }).ToList(),
      knots = knots,
      degree = target.Degree,
      periodic = target.IsPeriodic(out _),
      rational = target.IsRational,
      // domain = _intervalConverter.Convert(nurbsCurve.Domain),
      closed = target.IsClosed(),
      length = nurbsCurve.GetLength(),
      bbox = _boxConverter.Convert(new RG.Box(nurbsCurve.GetBoundingBox(true)))
    };

    return curve;
  }
}
