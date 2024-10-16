using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.ArcGIS3.ToHost.Raw;

public class PointListToHostConverter : ITypedConverter<List<SOG.Point>, ACG.Multipoint>
{
  private readonly ITypedConverter<SOG.Point, ACG.MapPoint> _pointConverter;

  public PointListToHostConverter(ITypedConverter<SOG.Point, ACG.MapPoint> pointConverter)
  {
    _pointConverter = pointConverter;
  }

  public ACG.Multipoint Convert(List<SOG.Point> target)
  {
    if (target.Count == 0)
    {
      throw new ValidationException("Feature contains no geometries");
    }
    List<ACG.MapPoint> pointList = new();
    foreach (SOG.Point pt in target)
    {
      pointList.Add(_pointConverter.Convert(pt));
    }
    return new ACG.MultipointBuilderEx(pointList, ACG.AttributeFlags.HasZ).ToGeometry();
  }
}
