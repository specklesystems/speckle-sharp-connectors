using Speckle.Converters.Civil3dShared.Helpers;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Civil3dShared.ToSpeckle.Raw;

public class AlignmentSubentityLineToSpeckleRawConverter : ITypedConverter<CDB.AlignmentSubEntityLine, SOG.Line>
{
  private readonly IConverterSettingsStore<Civil3dConversionSettings> _settingsStore;

  public AlignmentSubentityLineToSpeckleRawConverter(IConverterSettingsStore<Civil3dConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  public SOG.Line Convert(object target) => Convert((CDB.AlignmentSubEntityLine)target);

  public SOG.Line Convert(CDB.AlignmentSubEntityLine target)
  {
    SOG.Point start =
      new()
      {
        x = target.StartPoint.X,
        y = target.StartPoint.Y,
        z = 0,
        units = _settingsStore.Current.SpeckleUnits,
      };

    SOG.Point end =
      new()
      {
        x = target.EndPoint.X,
        y = target.EndPoint.Y,
        z = 0,
        units = _settingsStore.Current.SpeckleUnits,
      };

    SOG.Line line =
      new()
      {
        start = start,
        end = end,
        units = _settingsStore.Current.SpeckleUnits,
      };

    // create a properties dictionary for additional props
    PropertyHandler propHandler = new();
    Dictionary<string, object?> props =
      new() { ["startStation"] = target.StartStation, ["endStation"] = target.EndStation, };
    propHandler.TryAddToDictionary(props, "direction", () => target.Direction); // may throw
    line["properties"] = props;
    return line;
  }
}
