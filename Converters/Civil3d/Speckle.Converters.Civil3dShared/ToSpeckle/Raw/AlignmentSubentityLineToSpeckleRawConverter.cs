using Speckle.Converters.Civil3dShared.Helpers;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Civil3dShared.ToSpeckle.Raw;

public class AlignmentSubentityLineToSpeckleRawConverter : ITypedConverter<CDB.AlignmentSubEntityLine, SOG.Line>
{
  private readonly IConverterSettingsStore<Civil3dConversionSettings> _settingsStore;
  private readonly ITypedConverter<AG.Point2d, SOG.Point> _pointConverter;

  public AlignmentSubentityLineToSpeckleRawConverter(
    IConverterSettingsStore<Civil3dConversionSettings> settingsStore,
    ITypedConverter<AG.Point2d, SOG.Point> pointConverter
  )
  {
    _settingsStore = settingsStore;
    _pointConverter = pointConverter;
  }

  public SOG.Line Convert(object target) => Convert((CDB.AlignmentSubEntityLine)target);

  public SOG.Line Convert(CDB.AlignmentSubEntityLine target)
  {
    SOG.Point start = _pointConverter.Convert(target.StartPoint);
    SOG.Point end = _pointConverter.Convert(target.EndPoint);

    SOG.Line line =
      new()
      {
        start = start,
        end = end,
        units = _settingsStore.Current.SpeckleUnits
      };

    // create a properties dictionary for additional props
    PropertyHandler propHandler = new();
    Dictionary<string, object?> props =
      new() { ["startStation"] = target.StartStation, ["endStation"] = target.EndStation };
    propHandler.TryAddToDictionary(props, "direction", () => target.Direction); // may throw
    line["properties"] = props;
    return line;
  }
}
