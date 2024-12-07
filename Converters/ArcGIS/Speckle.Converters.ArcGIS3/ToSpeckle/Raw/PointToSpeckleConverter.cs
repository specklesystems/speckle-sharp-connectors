using System.ComponentModel.DataAnnotations;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk;

namespace Speckle.Converters.ArcGIS3.ToSpeckle.Raw;

public class PointToSpeckleConverter : ITypedConverter<ACG.MapPoint, SOG.Point>
{
  private readonly IConverterSettingsStore<ArcGISConversionSettings> _settingsStore;

  public PointToSpeckleConverter(IConverterSettingsStore<ArcGISConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  public SOG.Point Convert(ACG.MapPoint target)
  {
    try
    {
      // reproject to Active CRS
      if (
        ACG.GeometryEngine.Instance.Project(target, _settingsStore.Current.ActiveCRSoffsetRotation.SpatialReference)
        is not ACG.MapPoint reprojectedPt
      )
      {
        throw new ValidationException(
          $"Conversion to Spatial Reference {_settingsStore.Current.ActiveCRSoffsetRotation.SpatialReference.Name} failed"
        );
      }

      if (
        double.IsNaN(reprojectedPt.X)
        || double.IsInfinity(reprojectedPt.X)
        || double.IsNaN(reprojectedPt.Y)
        || double.IsInfinity(reprojectedPt.Y)
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
