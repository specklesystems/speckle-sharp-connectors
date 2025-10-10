using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Autocad.ToSpeckle.Raw;

public class DBCircleToSpeckleRawConverter : ITypedConverter<ADB.Circle, SOG.Circle>
{
  private readonly ITypedConverter<AG.Plane, SOG.Plane> _planeConverter;
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;

  public DBCircleToSpeckleRawConverter(
    ITypedConverter<AG.Plane, SOG.Plane> planeConverter,
    IConverterSettingsStore<AutocadConversionSettings> settingsStore
  )
  {
    _planeConverter = planeConverter;
    _settingsStore = settingsStore;
  }

  public Base Convert(object target) => Convert((ADB.Circle)target);

  public SOG.Circle Convert(ADB.Circle target)
  {
    SOG.Plane plane = _planeConverter.Convert(target.GetPlane());
    SOG.Circle circle =
      new()
      {
        plane = plane,
        radius = target.Radius,
        units = _settingsStore.Current.SpeckleUnits
      };

    return circle;
  }
}
