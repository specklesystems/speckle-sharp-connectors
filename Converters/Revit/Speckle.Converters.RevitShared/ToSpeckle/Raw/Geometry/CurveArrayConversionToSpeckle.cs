using Autodesk.Revit.DB;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Services;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Objects;
using Speckle.Objects.Geometry;

namespace Speckle.Converters.RevitShared.ToSpeckle;

public sealed class CurveArrayConversionToSpeckle : ITypedConverter<DB.CurveArray, SOG.Polycurve>
{
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private readonly ScalingServiceToSpeckle _scalingService;
  private readonly ITypedConverter<DB.Curve, ICurve> _curveConverter;

  public CurveArrayConversionToSpeckle(
    IConverterSettingsStore<RevitConversionSettings> converterSettings,
    ScalingServiceToSpeckle scalingService,
    ITypedConverter<DB.Curve, ICurve> curveConverter
  )
  {
    _converterSettings = converterSettings;
    _scalingService = scalingService;
    _curveConverter = curveConverter;
  }

  public Polycurve Convert(CurveArray target)
  {
    List<DB.Curve> curves = target.Cast<DB.Curve>().ToList();

    return new Polycurve()
    {
      units = _converterSettings.Current.SpeckleUnits,
      closed =
        curves.First().GetEndPoint(0).DistanceTo(curves.Last().GetEndPoint(1)) < _converterSettings.Current.Tolerance,
      length = _scalingService.ScaleLength(curves.Sum(x => x.Length)),
      segments = curves.Select(x => _curveConverter.Convert(x)).ToList()
    };
  }
}
