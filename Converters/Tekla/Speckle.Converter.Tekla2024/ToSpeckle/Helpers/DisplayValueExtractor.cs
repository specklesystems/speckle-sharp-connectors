using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using SOG = Speckle.Objects.Geometry;
using Speckle.Sdk.Models;

namespace Speckle.Converter.Tekla2024.ToSpeckle.Helpers;

public sealed class DisplayValueExtractor
{
  private readonly ITypedConverter<TSM.Solid, SOG.Mesh> _meshConverter;
  private readonly IConverterSettingsStore<TeklaConversionSettings> _settingsStore;

  public DisplayValueExtractor(
    ITypedConverter<TSM.Solid, SOG.Mesh> meshConverter,
    IConverterSettingsStore<TeklaConversionSettings> settingsStore)
  {
    _meshConverter = meshConverter;
    _settingsStore = settingsStore;
  }

  public IEnumerable<Base> GetDisplayValue(TSM.ModelObject modelObject)
  {
    switch (modelObject)
    {
      case TSM.Beam beam:
        var solid = beam.GetSolid();
        if (solid != null)
        {
          var mesh = _meshConverter.Convert(solid);
          yield return mesh;
        }
        break;

      // add cases for other model object types that need display values
      // case TSM.ContourPlate plate:
            
      default:
        yield break;
    }
  }
}
