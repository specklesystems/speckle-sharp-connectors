using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.TeklaShared.ToSpeckle.Raw;

public class TeklaPointConverter : ITypedConverter<TG.Point, SOG.Point>
{
  private readonly IConverterSettingsStore<TeklaConversionSettings> _settingsStore;

  public TeklaPointConverter(IConverterSettingsStore<TeklaConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  public SOG.Point Convert(TG.Point target) =>
    new SOG.Point(target.X, target.Y, target.Z, _settingsStore.Current.SpeckleUnits);
}
