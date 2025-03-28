using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Autocad.ToSpeckle.Raw;

public class NurbCurve3dToSpeckleConverter : ITypedConverter<AG.NurbCurve3d, SOG.Curve>
{
  private readonly ITypedConverter<AG.Point3d, SOG.Point> _pointConverter;
  private readonly ITypedConverter<AG.Plane, SOG.Plane> _planeConverter;
  private readonly ITypedConverter<ADB.Polyline, SOG.Autocad.AutocadPolycurve> _polylineConverter;
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;

  public NurbCurve3dToSpeckleConverter(
    ITypedConverter<AG.Point3d, SOG.Point> pointConverter,
    ITypedConverter<AG.Plane, SOG.Plane> planeConverter,
    ITypedConverter<ADB.Polyline, SOG.Autocad.AutocadPolycurve> polylineConverter,
    IConverterSettingsStore<AutocadConversionSettings> settingsStore
  )
  {
    _pointConverter = pointConverter;
    _planeConverter = planeConverter;
    _polylineConverter = polylineConverter;
    _settingsStore = settingsStore;
  }

  public SOG.Curve Convert(AG.NurbCurve3d target)
  {
    double startParam = target.GetParameterOf(target.StartPoint);
    double endParam = target.GetParameterOf(target.EndPoint);

    /*
    ADB.Polyline poly;
    if (target.IsClosed())
    {
      poly.AddVertex(poly[0]);
    }
    SOG.Polyline? displayPoly = poly == null ?? _polylineConverter.Convert(poly);
    */
    SOG.Polyline displayPoly = new() { value = [0, 0, 0, 1, 1, 1], units = "m" };

    List<AG.Point3d> points = new();
    List<double> weights = new();
    for (int i = 0; i < target.NumberOfControlPoints; i++)
    {
      points.Add(target.ControlPointAt(i));
      weights.Add(target.GetWeightAt(i)); // System.AccessViolationException: Attempted to read or write protected memory.
    }

    var knots = new List<double>();
    for (int i = 0; i < target.Knots.Count; i++)
    {
      knots.Add(target.KnotAt(i));
    }

    var curve = new SOG.Curve
    {
      displayValue = displayPoly,
      units = _settingsStore.Current.SpeckleUnits,
      weights = weights,
      points = points.SelectMany(ctp => new[] { ctp.X, ctp.Y, ctp.Z }).ToList(),
      knots = knots,
      degree = target.Degree,
      periodic = target.IsPeriodic(out _),
      rational = target.IsRational,
      domain = new SOP.Interval() { start = startParam, end = endParam },
      closed = target.IsClosed(),
      length = target.GetLength(startParam, endParam, 0.00001)
    };

    return curve;
  }
}
