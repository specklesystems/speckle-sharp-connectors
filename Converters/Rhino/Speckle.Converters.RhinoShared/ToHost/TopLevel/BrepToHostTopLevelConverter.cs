using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Rhino.ToHost.TopLevel;

[NameAndRankValue(nameof(SOG.Brep), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class BrepToHostTopLevelConverter : SpeckleToHostGeometryBaseTopLevelConverter<SOG.Brep, RG.Brep>
{
  public BrepToHostTopLevelConverter(
    IConverterSettingsStore<RhinoConversionSettings> settingsStore,
    ITypedConverter<SOG.Brep, RG.Brep> geometryBaseConverter
  )
    : base(settingsStore, geometryBaseConverter) { }
}
