using Speckle.Converters.Common.Objects;
using Speckle.Objects;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.ArcGIS3.ToHost.Raw;

public class ICurveListToHostConverter : ITypedConverter<List<ICurve>, ACG.Polyline>
{
  private readonly ITypedConverter<ICurve, ACG.Polyline> _icurveConverter;

  public ICurveListToHostConverter(ITypedConverter<ICurve, ACG.Polyline> icurveConverter)
  {
    _icurveConverter = icurveConverter;
  }

  public ACG.Polyline Convert(List<ICurve> target)
  {
    if (target.Count == 0)
    {
      throw new ValidationException("Feature contains no geometries");
    }
    List<ACG.Polyline> polyList = new();
    foreach (ICurve poly in target)
    {
      ACG.Polyline arcgisPoly = _icurveConverter.Convert(poly);
      polyList.Add(arcgisPoly);
    }
    return new ACG.PolylineBuilderEx(polyList, ACG.AttributeFlags.HasZ).ToGeometry();
  }
}
