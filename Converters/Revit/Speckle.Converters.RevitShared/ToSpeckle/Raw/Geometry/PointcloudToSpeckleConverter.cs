using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.RevitShared.ToSpeckle;

public sealed class PointcloudToSpeckleConverter : ITypedConverter<DB.PointCloudInstance, SOG.Pointcloud>
{
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private readonly ITypedConverter<DB.XYZ, SOG.Point> _xyzToPointConverter;
  private readonly ITypedConverter<DB.BoundingBoxXYZ, SOG.Box> _boundingBoxConverter;

  public PointcloudToSpeckleConverter(
    IConverterSettingsStore<RevitConversionSettings> converterSettings,
    ITypedConverter<DB.XYZ, SOG.Point> xyzToPointConverter,
    ITypedConverter<DB.BoundingBoxXYZ, SOG.Box> boundingBoxConverter
  )
  {
    _converterSettings = converterSettings;
    _xyzToPointConverter = xyzToPointConverter;
    _boundingBoxConverter = boundingBoxConverter;
  }

  public Base Convert(object target) => Convert((DB.PointCloudInstance)target);

  public SOG.Pointcloud Convert(DB.PointCloudInstance target)
  {
    var boundingBox = target.get_BoundingBox(null!); // the bounding box in the parent document
    var minPlane = DB.Plane.CreateByNormalAndOrigin(DB.XYZ.BasisZ, boundingBox.Min); // the lowest z plane from the bounding box min, in the parent doc
    using DB.Transform transform = target.GetTransform();
    {
      var filter = DB.PointClouds.PointCloudFilterFactory.CreateMultiPlaneFilter(new List<DB.Plane>() { minPlane });
      var points = target.GetPoints(filter, 0.0001, 999999); // max limit is 1 mil but 1000000 throws error
      if (points is null)
      {
        throw new ConversionException("No points found");
      }
      var specklePointCloud = new SOG.Pointcloud
      {
        points = points
          .Select(o => _xyzToPointConverter.Convert(transform.OfPoint(o))) // these points need to be transformed, since coords are in the pointcloud linked doc
          .SelectMany(o => new List<double>() { o.x, o.y, o.z })
          .ToList(),
        colors = points.Select(o => ConvertAbgrToArgb(o.Color)).ToList(),
        units = _converterSettings.Current.SpeckleUnits,
        bbox = _boundingBoxConverter.Convert(boundingBox),
      };

      return specklePointCloud;
    }
  }

  /// <summary>
  /// Converts a color from ABGR format (Revit's default) to ARGB format (our expected format).
  /// </summary>
  private static int ConvertAbgrToArgb(int abgr)
  {
    // ABGR -> ARGB
    // Alpha and Green stay in place, swap Red and Blue
    return (abgr & unchecked((int)0xFF00FF00)) // Keep Alpha and Green
      | ((abgr & 0x00FF0000) >> 16) // Move Blue to Red position
      | ((abgr & 0x000000FF) << 16); // Move Red to Blue position
  }
}
