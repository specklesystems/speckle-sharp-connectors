using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Common.Exceptions;

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
    ACG.MapPoint point;
    try
    {
      // reproject to Active CRS
      point = (ACG.MapPoint)
        ACG.GeometryEngine.Instance.Project(target, _settingsStore.Current.ActiveCRSoffsetRotation.SpatialReference);
    }
    catch (ArgumentNullException anEx)
    {
      throw new ConversionException("MapPoint was null", anEx);
    }
    catch (ArgumentException aEx)
    {
      throw new ConversionException("Spatial reference was not supported", aEx);
    }
    catch (NotImplementedException niEx)
    {
      throw new ConversionException("", niEx);
    }

    if (double.IsNaN(point.X) || double.IsInfinity(point.X) || double.IsNaN(point.Y) || double.IsInfinity(point.Y))
    {
      throw new ConversionException(
        $"Conversion to Spatial Reference {_settingsStore.Current.ActiveCRSoffsetRotation.SpatialReference.Name} failed: coordinates undefined"
      );
    }

    // convert to Speckle Pt
    SOG.Point reprojectedSpecklePt = new(point.X, point.Y, point.Z, _settingsStore.Current.SpeckleUnits);
    SOG.Point scaledMovedRotatedPoint = _settingsStore.Current.ActiveCRSoffsetRotation.OffsetRotateOnSend(
      reprojectedSpecklePt,
      _settingsStore.Current.SpeckleUnits
    );

    return scaledMovedRotatedPoint;
  }
}
