using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converter.Tekla2024.ToSpeckle.Raw;

public class VectorToSpeckleRawConverter : ITypedConverter<TG.Vector, SOG.Vector>
{
  private readonly IConverterSettingsStore<TeklaConversionSettings> _settingsStore;

  public VectorToSpeckleRawConverter(IConverterSettingsStore<TeklaConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  public SOG.Vector Convert(TG.Vector target) => new(target.X, target.Y, target.Z, _settingsStore.Current.SpeckleUnits);
}
