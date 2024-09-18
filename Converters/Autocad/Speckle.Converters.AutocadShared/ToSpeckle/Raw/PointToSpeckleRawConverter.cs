using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Autocad.ToSpeckle.Raw;

public class PointToSpeckleRawConverter : ITypedConverter<AG.Point3d, SOG.Point>
{
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;

  public PointToSpeckleRawConverter(IConverterSettingsStore<AutocadConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  public SOG.Point Convert(AG.Point3d target) => new(target.X, target.Y, target.Z, _settingsStore.Current.SpeckleUnits);
}
