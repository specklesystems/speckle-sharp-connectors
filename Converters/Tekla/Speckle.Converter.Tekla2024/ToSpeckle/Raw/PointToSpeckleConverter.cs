using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using SOG = Speckle.Objects.Geometry;

namespace Speckle.Converter.Tekla2024.ToSpeckle.Raw;

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
