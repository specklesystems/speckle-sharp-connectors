using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Rhino.ToHost.TopLevel;

[NameAndRankValue(typeof(SOG.Ellipse), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class EllipseToHostTopLevelConverter : SpeckleToHostGeometryBaseTopLevelConverter<SOG.Ellipse, RG.NurbsCurve>
{
  public EllipseToHostTopLevelConverter(
    IConverterSettingsStore<RhinoConversionSettings> settingsStore,
    ITypedConverter<SOG.Ellipse, RG.NurbsCurve> geometryBaseConverter
  )
    : base(settingsStore, geometryBaseConverter) { }
}
