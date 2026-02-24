using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Autocad.ToSpeckle.Raw;

public class DBLineToSpeckleRawConverter : ITypedConverter<ADB.Line, SOG.Line>
{
  private readonly ITypedConverter<AG.Point3d, SOG.Point> _pointConverter;
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;

  public DBLineToSpeckleRawConverter(
    ITypedConverter<AG.Point3d, SOG.Point> pointConverter,
    IConverterSettingsStore<AutocadConversionSettings> settingsStore
  )
  {
    _pointConverter = pointConverter;
    _settingsStore = settingsStore;
  }

  public Base Convert(object target) => Convert((ADB.Line)target);

  public SOG.Line Convert(ADB.Line target) =>
    new()
    {
      start = _pointConverter.Convert(target.StartPoint),
      end = _pointConverter.Convert(target.EndPoint),
      domain = new SOP.Interval { start = 0, end = target.Length },
      units = _settingsStore.Current.SpeckleUnits
    };
}
