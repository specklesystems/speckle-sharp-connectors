using System.Collections.Generic;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;
using Tekla.Structures.Model;
using Tekla.Structures.Model.UI;
using SOG = Speckle.Objects.Geometry;
using TG = Tekla.Structures.Geometry3d;

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

    var solid = target.GetSolid();
    var mesh = _meshConverter.Convert(solid);

    var color = new Color();
    ModelObjectVisualization.GetRepresentation(target, ref color);

    int r = (int)(color.Red * 255);
    int g = (int)(color.Green * 255);
    int b = (int)(color.Blue * 255);
    int argb = (255 << 24) | (r << 16) | (g << 8) | b;

    int vertexCount = mesh.vertices.Count / 3;
    
    mesh.colors = new List<int>(vertexCount);
    for (int i = 0; i < vertexCount; i++)
    {
      mesh.colors.Add(argb);
    }
        
    beamObject["displayValue"] = new List<Base> { mesh };
    
    return beamObject;
  }
}
