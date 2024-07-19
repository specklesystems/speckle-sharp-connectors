using Objects;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.ToHost.TopLevel;

namespace Speckle.Converters.RevitShared.ToHost.Raw.Geometry;

[NameAndRankValue(nameof(SOG.Polycurve), 0)]
public class PolycurveToDirectShapeConverterToHost : CurveToDirectShapeConverterToHostBase<SOG.Polycurve>
{
  public PolycurveToDirectShapeConverterToHost(
    IRevitConversionContextStack contextStack,
    ITypedConverter<ICurve, DB.CurveArray> curveConverter
  )
    : base(contextStack, curveConverter) { }
}
