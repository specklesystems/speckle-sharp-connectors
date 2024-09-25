using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Rhino.ToHost.TopLevel;

[NameAndRankValue(nameof(SOG.Circle), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class CircleToHostTopLevelConverter : SpeckleToHostGeometryBaseTopLevelConverter<SOG.Circle, RG.ArcCurve>
{
  public CircleToHostTopLevelConverter(
    IConverterSettingsStore<RhinoConversionSettings> settingsStore,
    ITypedConverter<SOG.Circle, RG.ArcCurve> geometryBaseConverter
  )
    : base(settingsStore, geometryBaseConverter) { }
}
