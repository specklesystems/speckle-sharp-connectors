using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converter.Tekla2024.ToSpeckle.Helpers;

public sealed class DisplayValueExtractor
{
  private readonly ITypedConverter<TSM.Solid, SOG.Mesh> _meshConverter;

  private readonly GridHandler _gridHandler;
  private readonly IConverterSettingsStore<TeklaConversionSettings> _settingsStore;
  private readonly ITypedConverter<TG.LineSegment, SOG.Line> _lineConverter;
  private readonly ITypedConverter<TG.Arc, SOG.Arc> _arcConverter;

  public DisplayValueExtractor(
    ITypedConverter<TSM.Solid, SOG.Mesh> meshConverter,
    ITypedConverter<TG.LineSegment, SOG.Line> lineConverter,
    ITypedConverter<TG.Arc, SOG.Arc> arcConverter
    IConverterSettingsStore<TeklaConversionSettings> settingsStore
  )
  {
    _meshConverter = meshConverter;
    _gridHandler = new GridHandler(lineConverter);
    _settingsStore = settingsStore;
    _lineConverter = lineConverter;
    _arcConverter = arcConverter;
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
        var rebarGeometries = reinforcement.GetRebarComplexGeometries(
          withHooks: true,
          withoutClashes: true,
          lengthAdjustments: true,
          TSM.Reinforcement.RebarGeometrySimplificationTypeEnum.RATIONALIZED
        );

        foreach (TSM.RebarComplexGeometry barGeometry in rebarGeometries)
        {
          foreach (var leg in barGeometry.Legs)
          {
            if (leg.Curve is TG.LineSegment legLine)
            {
              yield return _lineConverter.Convert(legLine);
            }
            else if (leg.Curve is TG.Arc legArc)
            {
              yield return _arcConverter.Convert(legArc);
            }
          }
        }

        break;

      case TSM.Grid grid:
        foreach (var gridLine in _gridHandler.GetGridLines(grid))
        {
          yield return gridLine;
        }
        break;
      // we can switch to volumetric using the logic below
      // case TSM.Reinforcement reinforcement:
      //   if (reinforcement.GetSolid() is TSM.Solid reinforcementSolid)
      //   {
      //     yield return _meshConverter.Convert(reinforcementSolid);
      //   }
      //   break;

      default:
        yield break;
    }
  }
}
