using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Rhino.ToHost.TopLevel;

[NameAndRankValue(nameof(SOG.Polyline), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class PolylineToHostTopLevelConverter
  : SpeckleToHostGeometryBaseTopLevelConverter<SOG.Polyline, RG.PolylineCurve>
{
  public PolylineToHostTopLevelConverter(
    IConverterSettingsStore<RhinoConversionSettings> settingsStore,
    ITypedConverter<SOG.Polyline, RG.PolylineCurve> geometryBaseConverter
  )
    : base(settingsStore, geometryBaseConverter) { }
}
