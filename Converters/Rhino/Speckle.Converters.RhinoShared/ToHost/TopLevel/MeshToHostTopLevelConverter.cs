using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Rhino.ToHost.TopLevel;

[NameAndRankValue(nameof(SOG.Mesh), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class MeshToHostTopLevelConverter : SpeckleToHostGeometryBaseTopLevelConverter<SOG.Mesh, RG.Mesh>
{
  public MeshToHostTopLevelConverter(
    IConverterSettingsStore<RhinoConversionSettings> settingsStore,
    ITypedConverter<SOG.Mesh, RG.Mesh> geometryBaseConverter
  )
    : base(settingsStore, geometryBaseConverter) { }
}
