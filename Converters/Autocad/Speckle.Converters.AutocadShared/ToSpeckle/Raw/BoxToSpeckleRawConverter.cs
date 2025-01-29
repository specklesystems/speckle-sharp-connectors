using Speckle.Converters.Common;
using static Speckle.Converters.Common.Result;

namespace Speckle.Converters.Autocad.ToSpeckle.Raw;

public class BoxToSpeckleRawConverter(
  ITypedConverter<AG.Plane, SOG.Plane> planeConverter,
  IConverterSettingsStore<AutocadConversionSettings> settingsStore
) : ITypedConverter<ADB.Extents3d, SOG.Box>
{
  public Result<SOG.Box> Convert(ADB.Extents3d target)
  {
    // get dimension intervals and volume
    SOP.Interval xSize = new() { start = target.MinPoint.X, end = target.MaxPoint.X };
    SOP.Interval ySize = new() { start = target.MinPoint.Y, end = target.MaxPoint.Y };
    SOP.Interval zSize = new() { start = target.MinPoint.Z, end = target.MaxPoint.Z };

    // get the base plane of the bounding box from extents and current UCS
    var ucs = settingsStore.Current.Document.Editor.CurrentUserCoordinateSystem.CoordinateSystem3d;
    AG.Plane acadPlane = new(target.MinPoint, ucs.Xaxis, ucs.Yaxis);
    if (planeConverter.Try(acadPlane, out var plane))
    {
      return plane.Failure<SOG.Box>();
    }

    SOG.Box box =
      new()
      {
        plane = plane.Value,
        xSize = xSize,
        ySize = ySize,
        zSize = zSize,
        units = settingsStore.Current.SpeckleUnits
      };

    return Success(box);
  }
}
