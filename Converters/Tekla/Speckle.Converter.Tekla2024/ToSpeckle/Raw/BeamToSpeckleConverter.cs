using Speckle.Converter.Tekla2024.ToSpeckle.Helpers;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;
using SOG = Speckle.Objects.Geometry;

namespace Speckle.Converter.Tekla2024.ToSpeckle.Raw;

public class BeamToSpeckleConverter : ITypedConverter<TSM.Beam, Base>
{
  private readonly IConverterSettingsStore<TeklaConversionSettings> _settingsStore;
  private readonly ITypedConverter<TG.Point, SOG.Point> _pointConverter;
  private readonly DisplayValueExtractor _displayValueExtractor;

  public BeamToSpeckleConverter(
    IConverterSettingsStore<TeklaConversionSettings> settingsStore,
    ITypedConverter<TG.Point, SOG.Point> pointConverter,
    DisplayValueExtractor displayValueExtractor
  )
  {
    _settingsStore = settingsStore;
    _pointConverter = pointConverter;
    _displayValueExtractor = displayValueExtractor;
  }

  public Base Convert(TSM.Beam target)
  {
    var beamObject = new Base
    {
      ["type"] = nameof(TSM.Beam),
      ["units"] = _settingsStore.Current.SpeckleUnits,
      ["profile"] = target.Profile.ProfileString,
      ["material"] = target.Material.MaterialString,
    };

    var displayValue = _displayValueExtractor.GetDisplayValue(target).ToList();
    if (displayValue.Count != 0)
    {
      beamObject["displayValue"] = displayValue;
    }

    return beamObject;
  }
}
