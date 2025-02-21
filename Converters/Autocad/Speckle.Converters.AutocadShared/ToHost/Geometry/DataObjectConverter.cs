using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.Data;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Autocad.ToHost.Geometry;

[NameAndRankValue(typeof(DataObject), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class DataObjectConverter : IToHostTopLevelConverter, ITypedConverter<DataObject, List<(ADB.Entity a, Base b)>>
{
  private readonly ITypedConverter<SOG.Arc, ADB.Arc> _arcConverter;
  private readonly ITypedConverter<SOG.BrepX, List<(ADB.Entity a, Base b)>> _brepXConverter;
  private readonly ITypedConverter<SOG.Circle, ADB.Circle> _circleConverter;
  private readonly ITypedConverter<SOG.Curve, ADB.Curve> _curveConverter;
  private readonly ITypedConverter<SOG.Ellipse, ADB.Ellipse> _ellipseConverter;
  private readonly ITypedConverter<SOG.ExtrusionX, List<(ADB.Entity a, Base b)>> _extrusionXConverter;
  private readonly ITypedConverter<SOG.Line, ADB.Line> _lineConverter;
  private readonly ITypedConverter<SOG.Mesh, ADB.PolyFaceMesh> _meshConverter;
  private readonly ITypedConverter<SOG.Point, ADB.DBPoint> _pointConverter;
  private readonly ITypedConverter<SOG.Polycurve, List<(ADB.Entity a, Base b)>> _polycurveConverter;
  private readonly ITypedConverter<SOG.Polyline, ADB.Polyline3d> _polylineConverter;
  private readonly ITypedConverter<SOG.SubDX, List<(ADB.Entity a, Base b)>> _subDXConverter;

  public DataObjectConverter(
    ITypedConverter<SOG.Arc, ADB.Arc> arcConverter,
    ITypedConverter<SOG.BrepX, List<(ADB.Entity a, Base b)>> brepXConverter,
    ITypedConverter<SOG.Circle, ADB.Circle> circleConverter,
    ITypedConverter<SOG.Curve, ADB.Curve> curveConverter,
    ITypedConverter<SOG.Ellipse, ADB.Ellipse> ellipseConverter,
    ITypedConverter<SOG.ExtrusionX, List<(ADB.Entity a, Base b)>> extrusionXConverter,
    ITypedConverter<SOG.Line, ADB.Line> lineConverter,
    ITypedConverter<SOG.Mesh, ADB.PolyFaceMesh> meshConverter,
    ITypedConverter<SOG.Point, ADB.DBPoint> pointConverter,
    ITypedConverter<SOG.Polycurve, List<(ADB.Entity, Base)>> polycurveConverter,
    ITypedConverter<SOG.Polyline, ADB.Polyline3d> polylineConverter,
    ITypedConverter<SOG.SubDX, List<(ADB.Entity a, Base b)>> subDXConverter
  )
  {
    _arcConverter = arcConverter;
    _brepXConverter = brepXConverter;
    _circleConverter = circleConverter;
    _curveConverter = curveConverter;
    _ellipseConverter = ellipseConverter;
    _extrusionXConverter = extrusionXConverter;
    _lineConverter = lineConverter;
    _meshConverter = meshConverter;
    _pointConverter = pointConverter;
    _polycurveConverter = polycurveConverter;
    _polylineConverter = polylineConverter;
    _subDXConverter = subDXConverter;
  }

  public object Convert(Base target) => Convert((DataObject)target);

  public List<(ADB.Entity a, Base b)> Convert(DataObject target)
  {
    var result = new List<(ADB.Entity a, Base b)>();
    foreach (var item in target.displayValue)
    {
      result.AddRange(ConvertDisplayObject(item));
    }
    return result;
  }

  public IEnumerable<(ADB.Entity a, Base b)> ConvertDisplayObject(Base displayObject)
  {
    switch (displayObject)
    {
      case SOG.Arc arc:
        yield return (_arcConverter.Convert(arc), arc);
        break;
      case SOG.BrepX brepX:
        foreach (var i in _brepXConverter.Convert(brepX))
        {
          yield return i;
        }
        break;
      case SOG.Circle circle:
        yield return (_circleConverter.Convert(circle), circle);
        break;
      case SOG.Curve curve:
        yield return (_curveConverter.Convert(curve), curve);
        break;
      case SOG.Ellipse ellipse:
        yield return (_ellipseConverter.Convert(ellipse), ellipse);
        break;
      case SOG.ExtrusionX extrusionX:
        foreach (var i in _extrusionXConverter.Convert(extrusionX))
        {
          yield return i;
        }
        break;
      case SOG.Line line:
        yield return (_lineConverter.Convert(line), line);
        break;
      case SOG.Mesh mesh:
        yield return (_meshConverter.Convert(mesh), mesh);
        break;
      case SOG.Point point:
        yield return (_pointConverter.Convert(point), point);
        break;
      case SOG.Polycurve polycurve:
        foreach (var i in _polycurveConverter.Convert(polycurve))
        {
          yield return i;
        }
        break;
      case SOG.Polyline polyline:
        yield return (_polylineConverter.Convert(polyline), polyline);
        break;
      case SOG.SubDX subDX:
        foreach (var i in _subDXConverter.Convert(subDX))
        {
          yield return i;
        }
        break;

      default:
        throw new ConversionException($"Found unsupported geometry: {displayObject.GetType()}");
    }
  }
}
