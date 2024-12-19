using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Settings;

namespace Speckle.Converters.RevitShared.ToSpeckle;

[NameAndRankValue(nameof(DB.PointCloudInstance), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public sealed class PointcloudTopLevelConverterToSpeckle : IToSpeckleTopLevelConverter
{
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private readonly ITypedConverter<DB.XYZ, SOG.Point> _xyzToPointConverter;
  private readonly ITypedConverter<DB.BoundingBoxXYZ, SOG.Box> _boundingBoxConverter;

  public PointcloudTopLevelConverterToSpeckle(
    IConverterSettingsStore<RevitConversionSettings> converterSettings,
    ITypedConverter<DB.XYZ, SOG.Point> xyzToPointConverter,
    ITypedConverter<DB.BoundingBoxXYZ, SOG.Box> boundingBoxConverter
  )
  {
    _converterSettings = converterSettings;
    _xyzToPointConverter = xyzToPointConverter;
    _boundingBoxConverter = boundingBoxConverter;
  }

  public BaseResult Convert(object target) => Convert((DB.PointCloudInstance)target);

  public BaseResult Convert(DB.PointCloudInstance target)
  {
    var boundingBox = target.get_BoundingBox(null!);
    using DB.Transform transform = target.GetTransform();
    {
      var minPlane = DB.Plane.CreateByNormalAndOrigin(DB.XYZ.BasisZ, transform.OfPoint(boundingBox.Min));
      var filter = DB.PointClouds.PointCloudFilterFactory.CreateMultiPlaneFilter(new List<DB.Plane>() { minPlane });
      var points = target.GetPoints(filter, 0.0001, 999999); // max limit is 1 mil but 1000000 throws error

      // POC: complaining about nullability
      var specklePointCloud = new SOG.Pointcloud
      {
        points = points
          .Select(o => _xyzToPointConverter.Convert(transform.OfPoint(o)))
          .SelectMany(o => new List<double>() { o.x, o.y, o.z })
          .ToList(),
        colors = points.Select(o => o.Color).ToList(),
        units = _converterSettings.Current.SpeckleUnits,
        bbox = _boundingBoxConverter.Convert(boundingBox)
      };

      specklePointCloud["category"] = target.Category?.Name;

      return BaseResult.Success(specklePointCloud);
    }
  }
}
