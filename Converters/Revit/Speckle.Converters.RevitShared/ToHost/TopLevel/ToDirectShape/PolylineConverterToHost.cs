using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.ToHost.TopLevel;
using Speckle.Objects;

namespace Speckle.Converters.RevitShared.ToHost.Raw.Geometry;

[NameAndRankValue(nameof(SOG.Polyline), 0)]
public class PolylineToDirectShapeConverterToHost : CurveToDirectShapeConverterToHostBase<SOG.Polyline>
{
  public PolylineToDirectShapeConverterToHost(
    IRevitConversionContextStack contextStack,
    ITypedConverter<ICurve, DB.CurveArray> curveConverter
  )
    : base(contextStack, curveConverter) { }
}
