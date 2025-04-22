using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Common;

namespace Speckle.Converters.TeklaShared.ToSpeckle.Raw;

public class TeklaPointConverter : ITypedConverter<TG.Point, SOG.Point>
{
  private readonly IConverterSettingsStore<TeklaConversionSettings> _settingsStore;

  public TeklaPointConverter(IConverterSettingsStore<TeklaConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  public SOG.Point Convert(TG.Point target)
  {
    double conversionFactor = Units.GetConversionFactor(Units.Millimeters, _settingsStore.Current.SpeckleUnits);

    return new SOG.Point(
      target.X * conversionFactor,
      target.Y * conversionFactor,
      target.Z * conversionFactor,
      _settingsStore.Current.SpeckleUnits
    );
  }
}
