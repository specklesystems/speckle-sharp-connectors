using Speckle.Converters.Common;
using static Speckle.Converters.Common.Result;

namespace Speckle.Converters.Autocad.ToSpeckle.Raw;

public class PlaneToSpeckleRawConverter(
  ITypedConverter<AG.Vector3d, SOG.Vector> vectorConverter,
  ITypedConverter<AG.Point3d, SOG.Point> pointConverter,
  IConverterSettingsStore<AutocadConversionSettings> settingsStore
) : ITypedConverter<AG.Plane, SOG.Plane>
{
  public Result<SOG.Plane> Convert(AG.Plane target)
  {
    if (!pointConverter.Try(target.PointOnPlane, out var origin))
    {
      return origin.Failure<SOG.Plane>();
    }
    if (!vectorConverter.Try(target.Normal, out var normal))
    {
      return normal.Failure<SOG.Plane>();
    }
    var coo = target.GetCoordinateSystem();
    if (!vectorConverter.Try(coo.Xaxis, out var xdir))
    {
      return normal.Failure<SOG.Plane>();
    }
    if (!vectorConverter.Try(coo.Yaxis, out var ydir))
    {
      return normal.Failure<SOG.Plane>();
    }
    return Success(
      new SOG.Plane
      {
        origin = origin.Value,
        normal = normal.Value,
        xdir = xdir.Value,
        ydir = ydir.Value,
        units = settingsStore.Current.SpeckleUnits,
      }
    );
  }
}
