using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converter.Tekla2024.ToSpeckle.Helpers;

public sealed class DisplayValueExtractor
{
  private readonly ITypedConverter<TSM.Solid, SOG.Mesh> _meshConverter;
  private readonly GridHandler _gridHandler;
  private readonly IConverterSettingsStore<TeklaConversionSettings> _settingsStore;

  public DisplayValueExtractor(
    ITypedConverter<TSM.Solid, SOG.Mesh> meshConverter,
    ITypedConverter<TG.LineSegment, SOG.Line> lineConverter,
    IConverterSettingsStore<TeklaConversionSettings> settingsStore
  )
  {
    _meshConverter = meshConverter;
    _gridHandler = new GridHandler(lineConverter);
    _settingsStore = settingsStore;
  }

  public IEnumerable<Base> GetDisplayValue(TSM.ModelObject modelObject)
  {
    switch (modelObject)
    {
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

      // logic to send reinforcement as solid
      case TSM.Reinforcement reinforcement:
        if (reinforcement.GetSolid() is TSM.Solid reinforcementSolid)
        {
          yield return _meshConverter.Convert(reinforcementSolid);
        }
        break;

      case TSM.Grid grid:
        foreach (var gridLine in _gridHandler.GetGridLines(grid))
        {
          yield return gridLine;
        }
        break;

      default:
        yield break;
    }
  }
}
