using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.TeklaShared.ToSpeckle.Raw;

public class ArcToSpeckleConverter : ITypedConverter<TG.Arc, SOG.Arc>
{
  private readonly IConverterSettingsStore<TeklaConversionSettings> _settingsStore;
  private readonly ITypedConverter<TG.Point, SOG.Point> _pointConverter;
  private readonly ITypedConverter<TG.Vector, SOG.Vector> _vectorConverter;

  public ArcToSpeckleConverter(
    IConverterSettingsStore<TeklaConversionSettings> settingsStore,
    ITypedConverter<TG.Point, SOG.Point> pointConverter,
    ITypedConverter<TG.Vector, SOG.Vector> vectorConverter
  )
  {
    _settingsStore = settingsStore;
    _pointConverter = pointConverter;
    _vectorConverter = vectorConverter;
  }

  public SOG.Arc Convert(TG.Arc target)
  {
    var yaxis = target.Normal.Cross(target.StartDirection);

    SOG.Plane plane =
      new()
      {
        origin = _pointConverter.Convert(target.CenterPoint),
        normal = _vectorConverter.Convert(target.Normal),
        xdir = _vectorConverter.Convert(target.StartDirection),
        ydir = _vectorConverter.Convert(yaxis),
        units = _settingsStore.Current.SpeckleUnits,
      };

    return new()
    {
      startPoint = _pointConverter.Convert(target.StartPoint),
      midPoint = _pointConverter.Convert(target.ArcMiddlePoint),
      endPoint = _pointConverter.Convert(target.EndPoint),
      plane = plane,
      units = _settingsStore.Current.SpeckleUnits,
    };
  }
}
