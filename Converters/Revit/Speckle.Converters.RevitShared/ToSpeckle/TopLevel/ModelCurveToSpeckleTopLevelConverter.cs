using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.RevitShared.ToSpeckle;

[NameAndRankValue(nameof(DB.ModelCurve), 0)]
public class ModelCurveToSpeckleTopLevelConverter : BaseTopLevelConverterToSpeckle<DB.ModelCurve, Base>
{
  private readonly ITypedConverter<DB.Curve, ICurve> _curveConverter;

  public ModelCurveToSpeckleTopLevelConverter(ITypedConverter<DB.Curve, ICurve> curveConverter)
  {
    _curveConverter = curveConverter;
  }

  public override Base Convert(DB.ModelCurve target)
  {
    ICurve? iCurve = _curveConverter.Convert(target.GeometryCurve);

    switch (iCurve)
    {
      case SOG.Line line:
        return new SOG.Revit.RevitLine(line);
      case SOG.Ellipse ellipse:
        return new SOG.Revit.RevitEllipse(ellipse);
      case SOG.Curve curve:
        return new SOG.Revit.RevitCurve(curve);
      case SOG.Arc arc:
        return new SOG.Revit.RevitArc(arc);

      default:
        throw new ConversionException(
          $"No Revit class available for ModelCurve of type {target.GeometryCurve.GetType()}"
        );
    }
  }
}
