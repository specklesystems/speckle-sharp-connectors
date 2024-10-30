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
      // both beam and contour plate are child classes of part
      // its simpler to use part for common methods
      case TSM.Part part:
        if (part.GetSolid() is TSM.Solid partSolid)
        {
          yield return _meshConverter.Convert(partSolid);
        }
        break;

      case TSM.BoltGroup boltGroup:
        if (boltGroup.GetSolid() is TSM.Solid boltSolid)
        {
          yield return _meshConverter.Convert(boltSolid);
        }
        break;

      case TSM.Reinforcement reinforcement:
        if (reinforcement.GetSolid() is TSM.Solid reinforcementSolid)
        {
          yield return _meshConverter.Convert(reinforcementSolid);
        }
        break;

      default:
        yield break;
    }
  }
}
