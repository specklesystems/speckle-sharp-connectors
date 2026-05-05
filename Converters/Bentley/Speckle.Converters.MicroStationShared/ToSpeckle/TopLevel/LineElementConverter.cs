using Speckle.Converter.MicroStation.Extensions;
using Speckle.Converter.MicroStation.Settings;
using Speckle.Converters.Common;
using Speckle.Objects.Geometry;
using Speckle.Sdk.Models;

namespace Speckle.Converter.MicroStation.ToSpeckle.TopLevel;

[NameAndRankValue(typeof(MSIDGN.LineElement), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class LineElementConverter(IConverterSettingsStore<MicroStationConversionSettings> settingsStore)
  : IToSpeckleTopLevelConverter
{
  public Base Convert(object target) => Convert((MSIDGN.LineElement)target);

  private Line Convert(MSIDGN.LineElement element)
  {
    var s = settingsStore.Current;
    return new Line
    {
      start = element.StartPoint.ToSpecklePoint(s.SpeckleUnits),
      end = element.EndPoint.ToSpecklePoint(s.SpeckleUnits),
      units = s.SpeckleUnits,
      applicationId = element.ID.ToString(),
    };
  }
}
