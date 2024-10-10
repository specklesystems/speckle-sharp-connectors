using System.ComponentModel.DataAnnotations;
using ArcGIS.Core.Geometry;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk;

namespace Speckle.Converters.ArcGIS3.ToSpeckle.Raw;

public class PointToSpeckleConverter : ITypedConverter<MapPoint, SOG.Point>
{
  private readonly IConverterSettingsStore<ArcGISConversionSettings> _settingsStore;

  public PointToSpeckleConverter(IConverterSettingsStore<ArcGISConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  public SOG.Point Convert(MapPoint target)
  {
    try
    {
      // reproject to Active CRS
      if (
        GeometryEngine.Instance.Project(target, _settingsStore.Current.ActiveCRSoffsetRotation.SpatialReference)
        is not MapPoint reprojectedPt
      )
      {
        throw new ValidationException(
          $"Conversion to Spatial Reference {_settingsStore.Current.ActiveCRSoffsetRotation.SpatialReference.Name} failed"
        );
      }

      if (
        Double.IsNaN(reprojectedPt.X)
        || Double.IsInfinity(reprojectedPt.X)
        || Double.IsNaN(reprojectedPt.Y)
        || Double.IsInfinity(reprojectedPt.Y)
      )
      {
        throw new ValidationException(
          $"Conversion to Spatial Reference {_settingsStore.Current.ActiveCRSoffsetRotation.SpatialReference.Name} failed: coordinates undefined"
        );
      }

      // convert to Speckle Pt
      SOG.Point reprojectedSpecklePt =
        new(reprojectedPt.X, reprojectedPt.Y, reprojectedPt.Z, _settingsStore.Current.SpeckleUnits);
      SOG.Point scaledMovedRotatedPoint = _settingsStore.Current.ActiveCRSoffsetRotation.OffsetRotateOnSend(
        reprojectedSpecklePt,
        _settingsStore.Current.SpeckleUnits
      );
      return scaledMovedRotatedPoint;
    }
    catch (ArgumentException ex)
    {
      throw new SpeckleException(
        $"Conversion to Spatial Reference {_settingsStore.Current.ActiveCRSoffsetRotation.SpatialReference.Name} failed",
        ex
      );
    }
  }
}
