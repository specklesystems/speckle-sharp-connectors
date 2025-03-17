using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Rhino.ToHost.TopLevel;

[NameAndRankValue(typeof(SOG.Polycurve), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class PolycurveToHostTopLevelConverter : SpeckleToHostGeometryBaseTopLevelConverter<SOG.Polycurve, RG.PolyCurve>
{
  public PolycurveToHostTopLevelConverter(
    IConverterSettingsStore<RhinoConversionSettings> settingsStore,
    ITypedConverter<SOG.Polycurve, RG.PolyCurve> geometryBaseConverter
  )
    : base(settingsStore, geometryBaseConverter) { }
}
