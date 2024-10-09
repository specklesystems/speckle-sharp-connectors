using Autodesk.Civil.DatabaseServices;
using Microsoft.Extensions.Logging;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects;

namespace Speckle.Converters.Civil3dShared.Helpers;

public sealed class BaseCurveExtractor
{
  private readonly ILogger<DisplayValueExtractor> _logger;
  private readonly IConverterSettingsStore<Civil3dConversionSettings> _converterSettings;
  private readonly ITypedConverter<AG.LineSegment3d, SOG.Line> _lineConverter;
  private readonly ITypedConverter<AG.CircularArc2d, SOG.Arc> _arcConverter;
  private readonly ITypedConverter<ADB.Curve, Objects.ICurve> _curveConverter;

  public BaseCurveExtractor(
    ITypedConverter<AG.LineSegment3d, SOG.Line> lineConverter,
    ITypedConverter<AG.CircularArc2d, SOG.Arc> arcConverter,
    ITypedConverter<ADB.Curve, Objects.ICurve> curveConverter,
    ILogger<DisplayValueExtractor> logger,
    IConverterSettingsStore<Civil3dConversionSettings> converterSettings
  )
  {
    _lineConverter = lineConverter;
    _arcConverter = arcConverter;
    _curveConverter = curveConverter;
    _logger = logger;
    _converterSettings = converterSettings;
  }

  public List<Speckle.Objects.ICurve> GetBaseCurve(CDB.Entity entity)
  {
    List<Speckle.Objects.ICurve> result = new();

    switch (entity)
    {
      // rant: if this is a pipe, the BaseCurve prop is fake news && will return a DB.line with start and endpoints set to [0,0,0] & [0,0,1]
      // do not use basecurve for pipes 😡
      case CDB.Pipe pipe:
        ICurve pipeCurve =
          pipe.SubEntityType == PipeSubEntityType.Straight
            ? _lineConverter.Convert(new AG.LineSegment3d(pipe.StartPoint, pipe.EndPoint))
            : _arcConverter.Convert(pipe.Curve2d);
        result.Add(pipeCurve);
        break;
      default:
        ICurve baseCurve = _curveConverter.Convert(entity.BaseCurve);
        result.Add(baseCurve);
        break;
    }

    return result;
  }
}
