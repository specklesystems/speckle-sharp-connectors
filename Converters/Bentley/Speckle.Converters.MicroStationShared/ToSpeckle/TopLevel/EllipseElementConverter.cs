using Speckle.Converter.MicroStation.Extensions;
using Speckle.Converter.MicroStation.Settings;
using Speckle.Converters.Common;
using Speckle.Objects.Geometry;
using Speckle.Objects.Primitive;
using Speckle.Sdk.Models;

namespace Speckle.Converter.MicroStation.ToSpeckle.TopLevel;

[NameAndRankValue(typeof(MSIDGN.EllipseElement), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class EllipseElementConverter(IConverterSettingsStore<MicroStationConversionSettings> settingsStore)
  : IToSpeckleTopLevelConverter
{
  public Base Convert(object target) => Convert((MSIDGN.EllipseElement)target);

  private Ellipse Convert(MSIDGN.EllipseElement element)
  {
    var s = settingsStore.Current;

    var center = element.CenterPoint.ToSpecklePoint(s.SpeckleUnits);
    var rot = element.Rotation;
    var normal = new Vector(rot.RowZ.X, rot.RowZ.Y, rot.RowZ.Z, s.SpeckleUnits);
    var xdir = new Vector(rot.RowX.X, rot.RowX.Y, rot.RowX.Z, s.SpeckleUnits);
    var ydir = new Vector(rot.RowY.X, rot.RowY.Y, rot.RowY.Z, s.SpeckleUnits);

    return new Ellipse
    {
      plane = new Plane
      {
        origin = center,
        normal = normal,
        xdir = xdir,
        ydir = ydir,
        units = s.SpeckleUnits,
      },
      firstRadius = element.PrimaryRadius,
      secondRadius = element.SecondaryRadius,
      domain = Interval.UnitInterval,
      units = s.SpeckleUnits,
      applicationId = element.ID.ToString(),
    };
  }
}
