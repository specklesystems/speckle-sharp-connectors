using Speckle.Converters.Common;
using Speckle.Sdk.Models;
using static Speckle.Converters.Common.Result;

namespace Speckle.Converters.Autocad.ToSpeckle.Raw;

public class DBCircleToSpeckleRawConverter(
  ITypedConverter<AG.Plane, SOG.Plane> planeConverter,
  ITypedConverter<ADB.Extents3d, SOG.Box> boxConverter,
  IConverterSettingsStore<AutocadConversionSettings> settingsStore
) : ITypedConverter<ADB.Circle, SOG.Circle>
{
  public Result<Base> Convert(object target) => Convert((ADB.Circle)target).Base();

  public Result<SOG.Circle> Convert(ADB.Circle target)
  {
    if (planeConverter.Try(target.GetPlane(), out var plane))
    {
      return plane.Failure<SOG.Circle>();
    }
    if (boxConverter.Try(target.GeometricExtents, out var bbox))
    {
      return plane.Failure<SOG.Circle>();
    }
    SOG.Circle circle =
      new()
      {
        plane = plane.Value,
        radius = target.Radius,
        units = settingsStore.Current.SpeckleUnits,
        bbox = bbox.Value
      };

    return Success(circle);
  }
}
