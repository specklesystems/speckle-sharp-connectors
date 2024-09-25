using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Rhino.ToHost.TopLevel;

[NameAndRankValue(nameof(SOG.Pointcloud), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class PointCloudToHostTopLevelConverter
  : SpeckleToHostGeometryBaseTopLevelConverter<SOG.Pointcloud, RG.PointCloud>
{
  public PointCloudToHostTopLevelConverter(
    IConverterSettingsStore<RhinoConversionSettings> settingsStore,
    ITypedConverter<SOG.Pointcloud, RG.PointCloud> geometryBaseConverter
  )
    : base(settingsStore, geometryBaseConverter) { }
}
