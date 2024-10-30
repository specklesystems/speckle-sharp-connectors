using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converter.Tekla2024.ToSpeckle.TopLevel;

[NameAndRankValue(nameof(TSM.ControlCircle), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class ControlCircleToSpeckleTopLevelConverter : IToSpeckleTopLevelConverter
{
  private readonly IConverterSettingsStore<TeklaConversionSettings> _settingsStore;
  private readonly ITypedConverter<TG.Point, SOG.Point> _pointConverter;
  private readonly ITypedConverter<TG.Vector, SOG.Vector> _vectorConverter;

  public ControlCircleToSpeckleTopLevelConverter(
    IConverterSettingsStore<TeklaConversionSettings> settingsStore,
    ITypedConverter<TG.Point, SOG.Point> pointConverter,
    ITypedConverter<TG.Vector, SOG.Vector> vectorConverter
  )
  {
    _settingsStore = settingsStore;
    _pointConverter = pointConverter;
    _vectorConverter = vectorConverter;
  }

  public Base Convert(object target) => Convert((TSM.ControlCircle)target);

  public SOG.Circle Convert(TSM.ControlCircle target)
  {
    SOG.Point center = _pointConverter.Convert(target.Point1);
    SOG.Point pointOn = _pointConverter.Convert(target.Point2);

    TG.Vector normalVector =
      new(target.Point3.X - target.Point1.X, target.Point3.Y - target.Point1.Y, target.Point3.Z - target.Point1.Z);
    TG.Vector xAxis =
      new(target.Point2.X - target.Point1.X, target.Point2.Y - target.Point1.Y, target.Point2.Z - target.Point1.Z);
    TG.Vector yAxis = normalVector.Cross(xAxis);

    SOG.Plane plane =
      new()
      {
        origin = center,
        normal = _vectorConverter.Convert(normalVector.GetNormal()),
        xdir = _vectorConverter.Convert(xAxis.GetNormal()),
        ydir = _vectorConverter.Convert(yAxis.GetNormal()),
        units = _settingsStore.Current.SpeckleUnits
      };

    return new()
    {
      radius = center.DistanceTo(pointOn),
      plane = plane,
      units = _settingsStore.Current.SpeckleUnits
    };
  }
}
