using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.TeklaShared.ToSpeckle.Helpers;

public sealed class DisplayValueExtractor
{
  private readonly ITypedConverter<TSM.Solid, SOG.Mesh> _meshConverter;
  private readonly ITypedConverter<TG.Point, SOG.Point> _pointConverter;
  private readonly ITypedConverter<TG.LineSegment, SOG.Line> _lineConverter;
  private readonly IConverterSettingsStore<TeklaConversionSettings> _settingsStore;
  private readonly ITypedConverter<TG.Arc, SOG.Arc> _arcConverter;
  private readonly ITypedConverter<TSM.Grid, IEnumerable<Base>> _gridConverter;

  public DisplayValueExtractor(
    ITypedConverter<TSM.Solid, SOG.Mesh> meshConverter,
    ITypedConverter<TG.Point, SOG.Point> pointConverter,
    ITypedConverter<TG.LineSegment, SOG.Line> lineConverter,
    ITypedConverter<TG.Arc, SOG.Arc> arcConverter,
    ITypedConverter<TSM.Grid, IEnumerable<Base>> gridConverter,
    IConverterSettingsStore<TeklaConversionSettings> settingsStore
  )
  {
    _meshConverter = meshConverter;
    _pointConverter = pointConverter;
    _lineConverter = lineConverter;
    _settingsStore = settingsStore;
    _lineConverter = lineConverter;
    _arcConverter = arcConverter;
    _gridConverter = gridConverter;
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

      case TSM.Reinforcement reinforcement:
        if (_settingsStore.Current.SendRebarsAsSolid)
        {
          if (reinforcement.GetSolid() is TSM.Solid reinforcementSolid)
          {
            yield return _meshConverter.Convert(reinforcementSolid);
          }
          else
          {
            throw new ConversionException("The type has no solid.");
          }
        }
        else
        {
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
        }

        break;

      case TSM.Grid grid:
        foreach (var gridLine in _gridConverter.Convert(grid))
        {
          yield return gridLine;
        }

        break;

      default:
        yield break;
    }
  }
}
