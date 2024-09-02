using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Converters.RevitShared.ToHost.TopLevel;
using Speckle.Objects;

namespace Speckle.Converters.RevitShared.ToHost.Raw.Geometry;

[NameAndRankValue(nameof(SOG.Polycurve), 0)]
public class PolycurveToDirectShapeConverterToHost : CurveToDirectShapeConverterToHostBase<SOG.Polycurve>
{
  public PolycurveToDirectShapeConverterToHost(
    ISettingsStore<RevitConversionSettings> settings,
    ITypedConverter<ICurve, DB.CurveArray> curveConverter
  )
    : base(settings, curveConverter) { }
}
