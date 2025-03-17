using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.TeklaShared.ToSpeckle.Raw;

public class VectorToSpeckleConverter : ITypedConverter<TG.Vector, SOG.Vector>
{
  private readonly IConverterSettingsStore<TeklaConversionSettings> _settingsStore;

  public VectorToSpeckleConverter(IConverterSettingsStore<TeklaConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  public SOG.Vector Convert(TG.Vector target) => new(target.X, target.Y, target.Z, _settingsStore.Current.SpeckleUnits);
}
