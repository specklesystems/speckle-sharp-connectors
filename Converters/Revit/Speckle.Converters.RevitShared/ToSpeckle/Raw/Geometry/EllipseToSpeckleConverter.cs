using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Services;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Objects.Primitive;

namespace Speckle.Converters.RevitShared.ToSpeckle;

public class EllipseToSpeckleConverter : ITypedConverter<DB.Ellipse, SOG.Ellipse>
{
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private readonly ITypedConverter<
    (DB.XYZ origin, DB.XYZ xDir, DB.XYZ yDir, DB.XYZ normal),
    SOG.Plane
  > _curveOriginToPlaneConverter;
  private readonly ScalingServiceToSpeckle _scalingService;

  public EllipseToSpeckleConverter(
    IConverterSettingsStore<RevitConversionSettings> converterSettings,
    ITypedConverter<(DB.XYZ origin, DB.XYZ xDir, DB.XYZ yDir, DB.XYZ normal), SOG.Plane> curveOriginToPlaneConverter,
    ScalingServiceToSpeckle scalingService
  )
  {
    _converterSettings = converterSettings;
    _curveOriginToPlaneConverter = curveOriginToPlaneConverter;
    _scalingService = scalingService;
  }

  public SOG.Ellipse Convert(DB.Ellipse target)
  {
    var trim = target.IsBound
      ? new Interval { start = target.GetEndParameter(0), end = target.GetEndParameter(1) }
      : null;

    return new SOG.Ellipse()
    {
      plane = _curveOriginToPlaneConverter.Convert(
        (target.Center, target.XDirection, target.YDirection, target.Normal)
      ),
      firstRadius = _scalingService.ScaleLength(target.RadiusX),
      secondRadius = _scalingService.ScaleLength(target.RadiusY),
      domain = Interval.UnitInterval,
      trimDomain = trim,
      length = _scalingService.ScaleLength(target.Length),
      units = _converterSettings.Current.SpeckleUnits,
    };
  }
}
