using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Common;

namespace Speckle.Converters.Autocad.ToHost.Raw;

public class PointToHostRawConverter : ITypedConverter<SOG.Point, AG.Point3d>
{
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;

  public PointToHostRawConverter(IConverterSettingsStore<AutocadConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  public AG.Point3d Convert(SOG.Point target)
  {
    double f = Units.GetConversionFactor(target.units, _settingsStore.Current.SpeckleUnits);
    AG.Point3d point = new(target.x * f, target.y * f, target.z * f);
    return point;
  }
}
