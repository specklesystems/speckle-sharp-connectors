using Speckle.Converters.Common;
using Speckle.Sdk.Common;
using static Speckle.Converters.Common.Result;

namespace Speckle.Converters.Autocad.ToHost.Geometry;

[NameAndRankValue(typeof(SOG.Circle), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class CircleToHostConverter(
  ITypedConverter<SOG.Point, AG.Point3d> pointConverter,
  ITypedConverter<SOG.Vector, AG.Vector3d> vectorConverter,
  IConverterSettingsStore<AutocadConversionSettings> settingsStore
) : ITypedConverter<SOG.Circle, ADB.Circle>
{
  public Result<ADB.Circle> Convert(SOG.Circle target)
  {
    if (!vectorConverter.Try(target.plane.normal, out Result<AG.Vector3d> normal))
    {
      return normal.Failure<ADB.Circle>();
    }
    if (!pointConverter.Try(target.plane.origin, out Result<AG.Point3d> origin))
    {
      return origin.Failure<ADB.Circle>();
    }
    double f = Units.GetConversionFactor(target.units, settingsStore.Current.SpeckleUnits);

    var radius = f * target.radius;
    return Success(new ADB.Circle(origin.Value, normal.Value, radius));
  }
}
