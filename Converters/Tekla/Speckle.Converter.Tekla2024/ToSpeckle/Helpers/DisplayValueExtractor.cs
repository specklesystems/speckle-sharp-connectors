using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;
using SOG = Speckle.Objects.Geometry;

namespace Speckle.Converter.Tekla2024.ToSpeckle.Helpers;

public sealed class DisplayValueExtractor
{
  private readonly ITypedConverter<TSM.Solid, SOG.Mesh> _meshConverter;
  private readonly IConverterSettingsStore<TeklaConversionSettings> _settingsStore;

  public DisplayValueExtractor(
    ITypedConverter<TSM.Solid, SOG.Mesh> meshConverter,
    IConverterSettingsStore<TeklaConversionSettings> settingsStore
  )
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

      case TSM.ContourPlate plate:
        var plateSolid = plate.GetSolid();
        if (plateSolid != null)
        {
          var mesh = _meshConverter.Convert(plateSolid);
          yield return mesh;
        }
        break;

      default:
        yield break;
    }
  }
}
