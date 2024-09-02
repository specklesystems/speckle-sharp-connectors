using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Converters.RevitShared.ToHost.TopLevel;
using Speckle.Objects;

namespace Speckle.Converters.RevitShared.ToHost.Raw.Geometry;

[NameAndRankValue(nameof(SOG.Curve), 0)]
public class CurveToDirectShapeConverterToHost : CurveToDirectShapeConverterToHostBase<SOG.Curve>
{
  public CurveToDirectShapeConverterToHost(
    ISettingsStore<RevitConversionSettings> settings,
    ITypedConverter<ICurve, DB.CurveArray> curveConverter
  )
    : base(settings, curveConverter) { }
}
