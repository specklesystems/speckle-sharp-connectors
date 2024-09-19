using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Converters.RevitShared.ToHost.TopLevel;
using Speckle.Objects;

namespace Speckle.Converters.RevitShared.ToHost.Raw.Geometry;

[NameAndRankValue(nameof(SOG.Circle), 0)]
public class CircleToDirectShapeConverterToHost : CurveToDirectShapeConverterToHostBase<SOG.Circle>
{
  public CircleToDirectShapeConverterToHost(
    IConverterSettingsStore<RevitConversionSettings> converterSettings,
    ITypedConverter<ICurve, DB.CurveArray> curveConverter
  )
    : base(converterSettings, curveConverter) { }
}
