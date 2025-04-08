using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Common;

namespace Speckle.Converters.TeklaShared.ToSpeckle.Raw;

public class VectorToSpeckleConverter : ITypedConverter<TG.Vector, SOG.Vector>
{
  private readonly IConverterSettingsStore<TeklaConversionSettings> _settingsStore;

  public VectorToSpeckleConverter(IConverterSettingsStore<TeklaConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  public SOG.Vector Convert(TG.Vector target)
  {
    double conversionFactor = Units.GetConversionFactor(Units.Millimeters, _settingsStore.Current.SpeckleUnits);
    return new(
      target.X * conversionFactor,
      target.Y * conversionFactor,
      target.Z * conversionFactor,
      _settingsStore.Current.SpeckleUnits
    );
  }
}
