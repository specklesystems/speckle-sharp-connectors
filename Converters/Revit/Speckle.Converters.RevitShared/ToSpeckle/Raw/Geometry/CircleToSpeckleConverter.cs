using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Services;
using Speckle.Converters.RevitShared.Settings;

namespace Speckle.Converters.RevitShared.ToSpeckle;

public class CircleToSpeckleConverter : ITypedConverter<DB.Arc, SOG.Circle>
{
  private readonly ISettingsStore<RevitConversionSettings> _settings;
  private readonly ITypedConverter<DB.Plane, SOG.Plane> _planeConverter;
  private readonly ScalingServiceToSpeckle _scalingService;

  public CircleToSpeckleConverter(
    ISettingsStore<RevitConversionSettings> settings,
    ITypedConverter<DB.Plane, SOG.Plane> planeConverter,
    ScalingServiceToSpeckle scalingService
  )
  {
    _settings = settings;
    _planeConverter = planeConverter;
    _scalingService = scalingService;
  }

  public SOG.Circle Convert(DB.Arc target)
  {
    // POC: should we check for arc of 360 and throw? Original CircleToSpeckle did not do this.

    // see https://forums.autodesk.com/t5/revit-api-forum/how-to-retrieve-startangle-and-endangle-of-arc-object/td-p/7637128
    var arcPlane = DB.Plane.CreateByNormalAndOrigin(target.Normal, target.Center);
    var c = new SOG.Circle()
    {
      plane = _planeConverter.Convert(arcPlane),
      radius = _scalingService.ScaleLength(target.Radius),
      units = _settings.Current.SpeckleUnits,
      length = _scalingService.ScaleLength(target.Length)
    };

    return c;
  }
}
