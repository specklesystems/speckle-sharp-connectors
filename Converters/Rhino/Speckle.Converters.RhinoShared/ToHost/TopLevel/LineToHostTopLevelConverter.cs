using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Rhino.ToHost.TopLevel;

[NameAndRankValue(typeof(SOG.Line), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class LineToHostTopLevelConverter : SpeckleToHostGeometryBaseTopLevelConverter<SOG.Line, RG.LineCurve>
{
  public LineToHostTopLevelConverter(
    IConverterSettingsStore<RhinoConversionSettings> settingsStore,
    ITypedConverter<SOG.Line, RG.LineCurve> geometryBaseConverter
  )
    : base(settingsStore, geometryBaseConverter) { }
}
