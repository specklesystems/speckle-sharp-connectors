using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;
using SOG = Speckle.Objects.Geometry;
using TG = Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;

namespace Speckle.Converter.Tekla2024.ToSpeckle.Raw;

public class BeamRawConverter: ITypedConverter<Beam, Base>
{
  private readonly IConverterSettingsStore<TeklaConversionSettings> _settingsStore;

  public BeamRawConverter(IConverterSettingsStore<TeklaConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  public Base Convert(Beam target)
  {
    var beamObject = new Base
    {
      ["type"] = nameof(Beam),
      ["units"] = _settingsStore.Current.SpeckleUnits,
      ["profile"] = target.Profile.ProfileString,
      ["material"] = target.Material.MaterialString,
    };
    
    return beamObject;
  }
}
