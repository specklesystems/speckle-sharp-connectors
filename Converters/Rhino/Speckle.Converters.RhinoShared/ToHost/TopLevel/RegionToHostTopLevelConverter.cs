using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Rhino.ToHost.TopLevel;

[NameAndRankValue(typeof(SOG.Region), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class RegionToHostTopLevelConverter : SpeckleToHostGeometryBaseTopLevelConverter<SOG.Region, RG.Hatch>
{
  public RegionToHostTopLevelConverter(
    IConverterSettingsStore<RhinoConversionSettings> settingsStore,
    ITypedConverter<SOG.Region, RG.Hatch> geometryBaseConverter
  )
    : base(settingsStore, geometryBaseConverter) { }
}
