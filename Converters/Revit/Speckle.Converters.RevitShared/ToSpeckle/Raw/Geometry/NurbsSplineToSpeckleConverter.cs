using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Services;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Objects.Primitive;

namespace Speckle.Converters.RevitShared.ToSpeckle;

public class NurbsSplineToSpeckleConverter : ITypedConverter<DB.NurbSpline, SOG.Curve>
{
  private readonly IRevitVersionConversionHelper _conversionHelper;
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private readonly ITypedConverter<DB.XYZ, SOG.Point> _xyzToPointConverter;
  private readonly ScalingServiceToSpeckle _scalingService;

  public NurbsSplineToSpeckleConverter(
    IRevitVersionConversionHelper conversionHelper,
    IConverterSettingsStore<RevitConversionSettings> converterSettings,
    ITypedConverter<DB.XYZ, SOG.Point> xyzToPointConverter,
    ScalingServiceToSpeckle scalingService
  )
  {
    _conversionHelper = conversionHelper;
    _converterSettings = converterSettings;
    _xyzToPointConverter = xyzToPointConverter;
    _scalingService = scalingService;
  }

  public SOG.Curve Convert(DB.NurbSpline target)
  {
    var units = _converterSettings.Current.SpeckleUnits;

    var points = new List<double>();
    foreach (var p in target.CtrlPoints)
    {
      var point = _xyzToPointConverter.Convert(p);
      points.AddRange(new List<double> { point.x, point.y, point.z });
    }

    var coords = target.Tessellate().SelectMany(xyz => _xyzToPointConverter.Convert(xyz).ToList()).ToList();

    return new SOG.Curve
    {
      weights = target.Weights.Cast<double>().ToList(),
      points = points,
      knots = target.Knots.Cast<double>().ToList(),
      degree = target.Degree,
      periodic = false,
      rational = target.isRational,
      closed = _conversionHelper.IsCurveClosed(target),
      units = units,
      domain = new Interval { start = target.GetEndParameter(0), end = target.GetEndParameter(1) },
      length = _scalingService.ScaleLength(target.Length),
      displayValue = new SOG.Polyline { value = coords, units = units },
    };
  }
}
