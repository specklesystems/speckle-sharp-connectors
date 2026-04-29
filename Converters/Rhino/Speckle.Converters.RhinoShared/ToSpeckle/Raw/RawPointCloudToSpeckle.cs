using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Rhino.ToSpeckle.Raw;

public class RawPointCloudToSpeckle : ITypedConverter<RG.PointCloud, SOG.Pointcloud>
{
  private readonly IConverterSettingsStore<RhinoConversionSettings> _settingsStore;

  public RawPointCloudToSpeckle(IConverterSettingsStore<RhinoConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  /// <summary>
  /// Converts a Rhino PointCloud object to a Speckle Pointcloud object.
  /// </summary>
  /// <param name="target">The Rhino PointCloud object to convert.</param>
  /// <returns>The converted Speckle Pointcloud object.</returns>
  public SOG.Pointcloud Convert(RG.PointCloud target) =>
    new()
    {
      points = target.GetPoints().SelectMany(pt => new[] { pt.X, pt.Y, pt.Z }).ToList(),
      colors = target.GetColors().Select(o => o.ToArgb()).ToList(),
      units = _settingsStore.Current.SpeckleUnits,
    };
}
