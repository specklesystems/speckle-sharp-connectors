using Speckle.Converters.Common;
using static Speckle.Converters.Common.Result;

namespace Speckle.Converters.Autocad.ToSpeckle.Raw;

public class DBEllipseToSpeckleRawConverter(
  ITypedConverter<AG.Plane, SOG.Plane> planeConverter,
  ITypedConverter<ADB.Extents3d, SOG.Box> boxConverter,
  IConverterSettingsStore<AutocadConversionSettings> settingsStore
) : ITypedConverter<ADB.Ellipse, SOG.Ellipse>
{
  public Result<SOG.Ellipse> Convert(ADB.Ellipse target)
  {
    if (!planeConverter.Try(new AG.Plane(target.Center, target.MajorAxis, target.MinorAxis), out var plane))
    {
      return plane.Failure<SOG.Ellipse>();
    }

    if (!boxConverter.Try(target.GeometricExtents, out var bbox))
    {
      return bbox.Failure<SOG.Ellipse>();
    }

    // the start and end param corresponds to start and end angle in radians
    SOP.Interval trim = new() { start = target.StartAngle, end = target.EndAngle };

    SOG.Ellipse ellipse =
      new()
      {
        plane = plane.Value,
        firstRadius = target.MajorRadius,
        secondRadius = target.MinorRadius,
        units = settingsStore.Current.SpeckleUnits,
        domain = new SOP.Interval { start = 0, end = Math.PI * 2 },
        trimDomain = trim,
        length = target.GetDistanceAtParameter(target.EndParam),
        bbox = bbox.Value
      };

    return Success(ellipse);
  }
}
