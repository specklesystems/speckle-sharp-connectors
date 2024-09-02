using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Services;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Objects.Primitive;

namespace Speckle.Converters.RevitShared.ToSpeckle;

public class EllipseToSpeckleConverter : ITypedConverter<DB.Ellipse, SOG.Ellipse>
{
  private readonly ISettingsStore<RevitConversionSettings> _settings;
  private readonly ITypedConverter<DB.Plane, SOG.Plane> _planeConverter;
  private readonly ScalingServiceToSpeckle _scalingService;

  public EllipseToSpeckleConverter(
    ISettingsStore<RevitConversionSettings> settings,
    ITypedConverter<DB.Plane, SOG.Plane> planeConverter,
    ScalingServiceToSpeckle scalingService
  )
  {
    _settings = settings;
    _planeConverter = planeConverter;
    _scalingService = scalingService;
  }

  public SOG.Ellipse Convert(DB.Ellipse target)
  {
    using (DB.Plane basePlane = DB.Plane.CreateByOriginAndBasis(target.Center, target.XDirection, target.YDirection))
    {
      var trim = target.IsBound
        ? new Interval { start = target.GetEndParameter(0), end = target.GetEndParameter(1) }
        : null;

      return new SOG.Ellipse()
      {
        plane = _planeConverter.Convert(basePlane),
        // POC: scale length correct? seems right?
        firstRadius = _scalingService.ScaleLength(target.RadiusX),
        secondRadius = _scalingService.ScaleLength(target.RadiusY),
        // POC: original EllipseToSpeckle() method was setting this twice
        domain = Interval.UnitInterval,
        trimDomain = trim,
        length = _scalingService.ScaleLength(target.Length),
        units = _settings.Current.SpeckleUnits
      };
    }
  }
}
