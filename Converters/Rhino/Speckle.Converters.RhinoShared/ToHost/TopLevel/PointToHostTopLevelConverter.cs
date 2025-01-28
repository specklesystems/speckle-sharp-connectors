using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Rhino.ToHost.TopLevel;

[NameAndRankValue(typeof(SOG.Point), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class PointToHostTopLevelConverter : SpeckleToHostGeometryBaseTopLevelConverter<SOG.Point, RG.Point>
{
  public PointToHostTopLevelConverter(
    IConverterSettingsStore<RhinoConversionSettings> settingsStore,
    ITypedConverter<SOG.Point, RG.Point> geometryBaseConverter
  )
    : base(settingsStore, geometryBaseConverter) { }
}
