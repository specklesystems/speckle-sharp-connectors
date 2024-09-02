using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Services;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Objects.Primitive;

namespace Speckle.Converters.RevitShared.ToSpeckle;

public class LineConversionToSpeckle : ITypedConverter<DB.Line, SOG.Line>
{
  private readonly ISettingsStore<RevitConversionSettings> _settings;
  private readonly ITypedConverter<DB.XYZ, SOG.Point> _xyzToPointConverter;
  private readonly ScalingServiceToSpeckle _scalingService;

  public LineConversionToSpeckle(
    ISettingsStore<RevitConversionSettings> settings,
    ITypedConverter<DB.XYZ, SOG.Point> xyzToPointConverter,
    ScalingServiceToSpeckle scalingService
  )
  {
    _settings = settings;
    _xyzToPointConverter = xyzToPointConverter;
    _scalingService = scalingService;
  }

  public SOG.Line Convert(DB.Line target) =>
    new()
    {
      units = _settings.Current.SpeckleUnits,
      start = _xyzToPointConverter.Convert(target.GetEndPoint(0)),
      end = _xyzToPointConverter.Convert(target.GetEndPoint(1)),
      domain = new Interval { start = target.GetEndParameter(0), end = target.GetEndParameter(1) },
      length = _scalingService.ScaleLength(target.Length)
    };
}
