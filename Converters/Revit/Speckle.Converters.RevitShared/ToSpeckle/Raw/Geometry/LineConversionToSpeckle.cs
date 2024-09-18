using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Services;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Objects.Primitive;

namespace Speckle.Converters.RevitShared.ToSpeckle;

public class LineConversionToSpeckle : ITypedConverter<DB.Line, SOG.Line>
{
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private readonly ITypedConverter<DB.XYZ, SOG.Point> _xyzToPointConverter;
  private readonly ScalingServiceToSpeckle _scalingService;

  public LineConversionToSpeckle(
    IConverterSettingsStore<RevitConversionSettings> converterSettings,
    ITypedConverter<DB.XYZ, SOG.Point> xyzToPointConverter,
    ScalingServiceToSpeckle scalingService
  )
  {
    _converterSettings = converterSettings;
    _xyzToPointConverter = xyzToPointConverter;
    _scalingService = scalingService;
  }

  public SOG.Line Convert(DB.Line target) =>
    new()
    {
      units = _converterSettings.Current.SpeckleUnits,
      start = _xyzToPointConverter.Convert(target.GetEndPoint(0)),
      end = _xyzToPointConverter.Convert(target.GetEndPoint(1)),
      domain = new Interval { start = target.GetEndParameter(0), end = target.GetEndParameter(1) },
    };
}
