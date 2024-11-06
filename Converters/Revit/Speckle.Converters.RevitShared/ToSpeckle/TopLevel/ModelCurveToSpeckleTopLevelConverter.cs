using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.RevitShared.ToSpeckle;

// Converts model curves to regular speckle curves, since we aren't receiving them and the only property used in V2 was the linestyle (not element ids or parameters). Don't see a need to handle these differently from regular geometry.
[NameAndRankValue(nameof(DB.ModelCurve), 0)]
public class ModelCurveToSpeckleTopLevelConverter : BaseTopLevelConverterToSpeckle<DB.ModelCurve, Base>
{
  private readonly ITypedConverter<DB.Curve, ICurve> _curveConverter;

  public ModelCurveToSpeckleTopLevelConverter(ITypedConverter<DB.Curve, ICurve> curveConverter)
  {
    _curveConverter = curveConverter;
  }

  public override Base Convert(DB.ModelCurve target) => (Base)_curveConverter.Convert(target.GeometryCurve);
}
