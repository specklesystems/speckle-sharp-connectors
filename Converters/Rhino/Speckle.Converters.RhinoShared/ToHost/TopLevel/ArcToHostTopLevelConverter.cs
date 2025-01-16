using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Rhino.ToHost.TopLevel;

[NameAndRankValue(typeof(SOG.Arc), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class ArcToHostTopLevelConverter : SpeckleToHostGeometryBaseTopLevelConverter<SOG.Arc, RG.ArcCurve>
{
  public ArcToHostTopLevelConverter(
    IConverterSettingsStore<RhinoConversionSettings> settingsStore,
    ITypedConverter<SOG.Arc, RG.ArcCurve> geometryBaseConverter
  )
    : base(settingsStore, geometryBaseConverter) { }
}
