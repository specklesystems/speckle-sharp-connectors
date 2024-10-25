using System.Collections.Generic;
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
  private readonly ITypedConverter<TG.Point, SOG.Point> _pointConverter;
  private readonly ITypedConverter<Solid, SOG.Mesh> _meshConverter;
  
  public BeamRawConverter(
    IConverterSettingsStore<TeklaConversionSettings> settingsStore, 
    ITypedConverter<TG.Point, SOG.Point> pointConverter,
    ITypedConverter<Solid, SOG.Mesh> meshConverter
    )
  {
    _settingsStore = settingsStore;
    _pointConverter = pointConverter;
    _meshConverter = meshConverter;
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
    
    var centerline = new SOG.Line
    {
      start = _pointConverter.Convert(target.StartPoint),
      end = _pointConverter.Convert(target.EndPoint),
      units = _settingsStore.Current.SpeckleUnits
    };

    var solid = target.GetSolid();
    var mesh = _meshConverter.Convert(solid);
        
    beamObject["displayValue"] = new List<Base> { centerline, mesh };
    
    return beamObject;
  }
}
