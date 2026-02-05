using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Instances;

namespace Speckle.Converters.Rhino.ToSpeckle.Grasshopper;

[NameAndRankValue(typeof(RG.GeometryBase), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class GeometryBaseConverter : IToSpeckleTopLevelConverter
{
  private readonly ITypedConverter<RG.Point, SOG.Point> _pointConverter;
  private readonly ITypedConverter<RG.ArcCurve, Base> _arcCurveConverter;
  private readonly ITypedConverter<RG.Hatch, SOG.Region> _hatchConverter;
  private readonly ITypedConverter<RG.InstanceReferenceGeometry, InstanceProxy> _instanceConverter;
  private readonly ITypedConverter<RG.LineCurve, SOG.Line> _lineCurveConverter;
  private readonly ITypedConverter<RG.NurbsCurve, SOG.Curve> _nurbsCurveConverter;
  private readonly ITypedConverter<RG.PointCloud, SOG.Pointcloud> _pointcloudConverter;
  private readonly ITypedConverter<RG.PolyCurve, SOG.Polycurve> _polycurveConverter;
  private readonly ITypedConverter<RG.Polyline, SOG.Polyline> _polylineConverter;
  private readonly ITypedConverter<RG.TextEntity, SA.Text> _textConverter;
  private readonly ITypedConverter<RG.Mesh, SOG.Mesh> _meshConverter;
  private readonly ITypedConverter<RG.Extrusion, SOG.ExtrusionX> _extrusionConverter;
  private readonly ITypedConverter<RG.SubD, SOG.SubDX> _subdConverter;
  private readonly ITypedConverter<RG.Brep, SOG.BrepX> _brepConverter;

  public GeometryBaseConverter(
    ITypedConverter<RG.Point, SOG.Point> pointConverter,
    ITypedConverter<RG.ArcCurve, Base> arcCurveConverter,
    ITypedConverter<RG.Hatch, SOG.Region> hatchConverter,
    ITypedConverter<RG.InstanceReferenceGeometry, InstanceProxy> instanceConverter,
    ITypedConverter<RG.LineCurve, SOG.Line> lineCurveConverter,
    ITypedConverter<RG.NurbsCurve, SOG.Curve> nurbsCurveConverter,
    ITypedConverter<RG.PointCloud, SOG.Pointcloud> pointcloudConverter,
    ITypedConverter<RG.PolyCurve, SOG.Polycurve> polycurveConverter,
    ITypedConverter<RG.Polyline, SOG.Polyline> polylineConverter,
    ITypedConverter<RG.TextEntity, SA.Text> textConverter,
    ITypedConverter<RG.Mesh, SOG.Mesh> meshConverter,
    ITypedConverter<RG.Brep, SOG.BrepX> brepConverter,
    ITypedConverter<RG.Extrusion, SOG.ExtrusionX> extrusionConverter,
    ITypedConverter<RG.SubD, SOG.SubDX> subdConverter
  )
  {
    _pointConverter = pointConverter;
    _arcCurveConverter = arcCurveConverter;
    _hatchConverter = hatchConverter;
    _instanceConverter = instanceConverter;
    _lineCurveConverter = lineCurveConverter;
    _nurbsCurveConverter = nurbsCurveConverter;
    _pointcloudConverter = pointcloudConverter;
    _polycurveConverter = polycurveConverter;
    _polylineConverter = polylineConverter;
    _textConverter = textConverter;
    _meshConverter = meshConverter;
    _brepConverter = brepConverter;
    _extrusionConverter = extrusionConverter;
    _subdConverter = subdConverter;
  }

  public Base Convert(object target)
  {
    return target switch
    {
      RG.Point pt => _pointConverter.Convert(pt),
      RG.ArcCurve ac => _arcCurveConverter.Convert(ac),
      RG.Hatch hatch => _hatchConverter.Convert(hatch),
      RG.InstanceReferenceGeometry instance => _instanceConverter.Convert(instance),
      RG.LineCurve ln => _lineCurveConverter.Convert(ln),
      RG.NurbsCurve nurbsCurve => _nurbsCurveConverter.Convert(nurbsCurve),
      RG.PointCloud pointcloud => _pointcloudConverter.Convert(pointcloud),
      RG.PolyCurve polyCurve => _polycurveConverter.Convert(polyCurve),
      RG.Polyline polyline => _polylineConverter.Convert(polyline),
      RG.PolylineCurve polylineCurve => _polylineConverter.Convert(polylineCurve.ToPolyline()),
      RG.TextEntity text => _textConverter.Convert(text),
      RG.Mesh mesh => _meshConverter.Convert(mesh),
      RG.Brep brep => _brepConverter.Convert(brep),
      RG.Extrusion ext => _extrusionConverter.Convert(ext),
      RG.SubD subD => _subdConverter.Convert(subD),
      _ => throw new ConversionException($"Failed to find a conversion for {target.GetType()}"),
    };
  }
}
