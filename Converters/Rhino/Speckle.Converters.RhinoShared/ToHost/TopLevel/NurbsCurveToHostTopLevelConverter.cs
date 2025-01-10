using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Rhino.ToHost.TopLevel;

[NameAndRankValue(typeof(SOG.Curve), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class NurbsCurveToHostTopLevelConverter : SpeckleToHostGeometryBaseTopLevelConverter<SOG.Curve, RG.NurbsCurve>
{
  public NurbsCurveToHostTopLevelConverter(
    IConverterSettingsStore<RhinoConversionSettings> settingsStore,
    ITypedConverter<SOG.Curve, RG.NurbsCurve> geometryBaseConverter
  )
    : base(settingsStore, geometryBaseConverter) { }
}
