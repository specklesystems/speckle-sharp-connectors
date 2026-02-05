using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Services;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Objects;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.RevitShared.Raw;

public sealed class ModelCurveArrayToSpeckleConverter : ITypedConverter<DB.ModelCurveArray, SOG.Polycurve>
{
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private readonly IScalingServiceToSpeckle _scalingService;
  private readonly ITypedConverter<DB.Curve, ICurve> _curveConverter;

  public ModelCurveArrayToSpeckleConverter(
    IConverterSettingsStore<RevitConversionSettings> converterSettings,
    IScalingServiceToSpeckle scalingService,
    ITypedConverter<DB.Curve, ICurve> curveConverter
  )
  {
    _converterSettings = converterSettings;
    _scalingService = scalingService;
    _curveConverter = curveConverter;
  }

  public SOG.Polycurve Convert(DB.ModelCurveArray target)
  {
    var curves = target.Cast<DB.ModelCurve>().Select(mc => mc.GeometryCurve).ToArray();

    if (curves.Length == 0)
    {
      throw new ValidationException($"Expected {target} to have at least 1 curve");
    }

    var start = curves[0].GetEndPoint(0);
    var end = curves[^1].GetEndPoint(1);
    SOG.Polycurve polycurve =
      new()
      {
        units = _converterSettings.Current.SpeckleUnits,
        closed = start.DistanceTo(end) < _converterSettings.Current.Tolerance,
        length = _scalingService.ScaleLength(curves.Sum(x => x.Length)),
        segments = curves.Select(x => _curveConverter.Convert(x)).ToList(),
      };

    return polycurve;
  }
}
