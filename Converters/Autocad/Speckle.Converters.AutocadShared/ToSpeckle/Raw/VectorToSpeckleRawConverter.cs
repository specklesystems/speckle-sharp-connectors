using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Autocad.ToSpeckle.Raw;

public class VectorToSpeckleRawConverter : ITypedConverter<AG.Vector3d, SOG.Vector>
{
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;

  public VectorToSpeckleRawConverter(IConverterSettingsStore<AutocadConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  public SOG.Vector Convert(AG.Vector3d target) =>
    new(target.X, target.Y, target.Z, _settingsStore.Current.SpeckleUnits);
}
