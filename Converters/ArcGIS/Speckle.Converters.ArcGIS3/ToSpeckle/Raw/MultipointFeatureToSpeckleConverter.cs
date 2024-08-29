using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.ArcGIS3.ToSpeckle.Raw;

public class MultipointFeatureToSpeckleConverter : ITypedConverter<ACG.Multipoint, IReadOnlyList<SOG.Point>>
{
  private readonly ITypedConverter<ACG.MapPoint, SOG.Point> _pointConverter;

  public MultipointFeatureToSpeckleConverter(ITypedConverter<ACG.MapPoint, SOG.Point> pointConverter)
  {
    _pointConverter = pointConverter;
  }

  public IReadOnlyList<SOG.Point> Convert(ACG.Multipoint target)
  {
    List<SOG.Point> multipoint = new();
    foreach (ACG.MapPoint point in target.Points)
    {
      ACG.MapPoint newPt = new ACG.MapPointBuilderEx(point.X, point.Y, point.Z, target.SpatialReference).ToGeometry();
      multipoint.Add(_pointConverter.Convert(newPt));
    }

    return multipoint;
  }
}
