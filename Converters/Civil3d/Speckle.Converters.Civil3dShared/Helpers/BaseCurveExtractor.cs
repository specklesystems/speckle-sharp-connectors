using Microsoft.Extensions.Logging;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Civil3dShared.Helpers;

public sealed class BaseCurveExtractor
{
  private readonly ILogger<DisplayValueExtractor> _logger;
  private readonly IConverterSettingsStore<Civil3dConversionSettings> _converterSettings;
  private readonly ITypedConverter<AG.LineSegment3d, SOG.Line> _lineConverter;
  private readonly ITypedConverter<AG.CircularArc2d, SOG.Arc> _arcConverter;
  private readonly ITypedConverter<CDB.AlignmentSubEntityLine, SOG.Line> _alignmentLineConverter;
  private readonly ITypedConverter<CDB.AlignmentSubEntityArc, SOG.Arc> _alignmentArcConverter;
  private readonly ITypedConverter<
    (CDB.AlignmentSubEntitySpiral, CDB.Alignment),
    SOG.Polyline
  > _alignmentSpiralConverter;
  private readonly ITypedConverter<ADB.Curve, Objects.ICurve> _curveConverter;

  public BaseCurveExtractor(
    ITypedConverter<AG.LineSegment3d, SOG.Line> lineConverter,
    ITypedConverter<AG.CircularArc2d, SOG.Arc> arcConverter,
    ITypedConverter<CDB.AlignmentSubEntityLine, SOG.Line> alignmentLineConverter,
    ITypedConverter<CDB.AlignmentSubEntityArc, SOG.Arc> alignmentArcConverter,
    ITypedConverter<(CDB.AlignmentSubEntitySpiral, CDB.Alignment), SOG.Polyline> alignmentSpiralConverter,
    ITypedConverter<ADB.Curve, Objects.ICurve> curveConverter,
    ILogger<DisplayValueExtractor> logger,
    IConverterSettingsStore<Civil3dConversionSettings> converterSettings
  )
  {
    _lineConverter = lineConverter;
    _arcConverter = arcConverter;
    _alignmentLineConverter = alignmentLineConverter;
    _alignmentArcConverter = alignmentArcConverter;
    _alignmentSpiralConverter = alignmentSpiralConverter;
    _curveConverter = curveConverter;
    _logger = logger;
    _converterSettings = converterSettings;
  }

  public List<Speckle.Objects.ICurve>? GetBaseCurves(CDB.Entity entity)
  {
    switch (entity)
    {
      // rant: if this is a pipe, the BaseCurve prop is fake news && will return a DB.line with start and endpoints set to [0,0,0] & [0,0,1]
      // pressurepipes also tend to have null basecurves
      // do not use basecurve for pipes 😡
      case CDB.Pipe pipe:
        return GetPipeBaseCurves(pipe);
#if CIVIL3D2024_OR_GREATER
      case CDB.PressurePipe pressurePipe:
        return GetPipeBaseCurves(pressurePipe);
#endif

      case CDB.Alignment alignment:
        return GetAlignmentBaseCurves(alignment);

      case CDB.FeatureLine:
      case CDB.Parcel:
      case CDB.ParcelSegment:
      case CDB.Catchment:
        return new() { _curveConverter.Convert(entity.BaseCurve) };

      // for any entities where basecurve prop doesn't make sense
      default:
        return null;
    }
  }

  private List<Speckle.Objects.ICurve> GetPipeBaseCurves(CDB.Pipe pipe)
  {
    switch (pipe.SubEntityType)
    {
      case CDB.PipeSubEntityType.Curved:
        return new() { _arcConverter.Convert(pipe.Curve2d) };

      // POC: don't know how to properly handle segmented and flex pipes for now, sending them as lines
      case CDB.PipeSubEntityType.Straight:
      default:
        return new() { _lineConverter.Convert(new AG.LineSegment3d(pipe.StartPoint, pipe.EndPoint)) };
    }
  }

#if CIVIL3D2024_OR_GREATER
  private List<Speckle.Objects.ICurve> GetPipeBaseCurves(CDB.PressurePipe pipe)
  {
    return pipe.IsCurve
      ? new() { _arcConverter.Convert(pipe.CurveGeometry.GetArc2d()) }
      : new() { _lineConverter.Convert(new AG.LineSegment3d(pipe.StartPoint, pipe.EndPoint)) };
  }
#endif

  private List<Speckle.Objects.ICurve> GetAlignmentBaseCurves(CDB.Alignment alignment)
  {
    // get the alignment subentity curves
    List<Speckle.Objects.ICurve> curves = new();
    for (int i = 0; i < alignment.Entities.Count; i++)
    {
      CDB.AlignmentEntity entity = alignment.Entities.GetEntityByOrder(i);
      for (int j = 0; j < entity.SubEntityCount; j++)
      {
        CDB.AlignmentSubEntity subEntity = entity[j];
        switch (subEntity.SubEntityType)
        {
          case CDB.AlignmentSubEntityType.Arc:
            if (subEntity is CDB.AlignmentSubEntityArc arc)
            {
              curves.Add(_alignmentArcConverter.Convert(arc));
            }
            break;
          case CDB.AlignmentSubEntityType.Line:
            if (subEntity is CDB.AlignmentSubEntityLine line)
            {
              curves.Add(_alignmentLineConverter.Convert(line));
            }
            break;
          case CDB.AlignmentSubEntityType.Spiral:
            if (subEntity is CDB.AlignmentSubEntitySpiral spiral)
            {
              curves.Add(_alignmentSpiralConverter.Convert((spiral, alignment)));
            }
            break;
        }
      }
    }

    return curves;
  }
}
