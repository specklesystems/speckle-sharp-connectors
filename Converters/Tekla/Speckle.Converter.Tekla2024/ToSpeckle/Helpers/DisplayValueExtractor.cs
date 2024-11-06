using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converter.Tekla2024.ToSpeckle.Helpers;

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

      // this section visualizes the rebars as solid
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
        foreach (var gridLine in _gridConverter.Convert(grid))
        {
          yield return gridLine;
        }

        break;

      // use this section to visualize rebars as lines
      // case TSM.SingleRebar singleRebar:
      //   if (singleRebar.Polygon is TSM.Polygon rebarPolygon)
      //   {
      //     for (int i = 0; i < rebarPolygon.Points.Count - 1; i++)
      //     {
      //       var startPoint = (TG.Point)rebarPolygon.Points[i];
      //       var endPoint = (TG.Point)rebarPolygon.Points[i + 1];
      //       var line = new TG.LineSegment(startPoint, endPoint);
      //
      //       var speckleLine = _lineConverter.Convert(line);
      //       speckleLine.start = _pointConverter.Convert(startPoint);
      //       speckleLine.end = _pointConverter.Convert(endPoint);
      //
      //       yield return speckleLine;
      //     }
      //   }

      //break;

      default:
        yield break;
    }
  }
}
