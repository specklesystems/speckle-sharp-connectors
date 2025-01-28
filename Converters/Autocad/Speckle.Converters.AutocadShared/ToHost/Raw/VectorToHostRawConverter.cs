using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Common;

namespace Speckle.Converters.Autocad.ToHost.Raw;

public class VectorToHostRawConverter : ITypedConverter<SOG.Vector, AG.Vector3d>
{
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;

  public VectorToHostRawConverter(IConverterSettingsStore<AutocadConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  public AG.Vector3d Convert(SOG.Vector target)
  {
    double f = Units.GetConversionFactor(target.units, _settingsStore.Current.SpeckleUnits);
    return new(target.x * f, target.y * f, target.z * f);
  }
}
