using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Rhino.ToSpeckle.Grasshopper;

[NameAndRankValue(typeof(RG.GeometryBase), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class GeometryBaseConverter : IToSpeckleTopLevelConverter
{
  private readonly ITypedConverter<RG.Point, SOG.Point> _pointConverter;
  private readonly ITypedConverter<RG.ArcCurve, Base> _arcCurveConverter;
  private readonly ITypedConverter<RG.Hatch, SOG.Region> _hatchConverter;
  private readonly ITypedConverter<RG.LineCurve, SOG.Line> _lineCurveConverter;
  private readonly ITypedConverter<RG.NurbsCurve, SOG.Curve> _nurbsCurveConverter;
  private readonly ITypedConverter<RG.PointCloud, SOG.Pointcloud> _pointcloudConverter;
  private readonly ITypedConverter<RG.PolyCurve, SOG.Polycurve> _polycurveConverter;
  private readonly ITypedConverter<RG.Polyline, SOG.Polyline> _polylineConverter;
  private readonly ITypedConverter<RG.Mesh, SOG.Mesh> _meshConverter;
  private readonly ITypedConverter<RG.Extrusion, SOG.ExtrusionX> _extrusionConverter;
  private readonly ITypedConverter<RG.SubD, SOG.SubDX> _subdConverter;
  private readonly ITypedConverter<RG.Brep, SOG.BrepX> _brepConverter;

  public GeometryBaseConverter(
    ITypedConverter<RG.Point, SOG.Point> pointConverter,
    ITypedConverter<RG.ArcCurve, Base> arcCurveConverter,
    ITypedConverter<RG.Hatch, SOG.Region> hatchConverter,
    ITypedConverter<RG.LineCurve, SOG.Line> lineCurveConverter,
    ITypedConverter<RG.NurbsCurve, SOG.Curve> nurbsCurveConverter,
    ITypedConverter<RG.PointCloud, SOG.Pointcloud> pointcloudConverter,
    ITypedConverter<RG.PolyCurve, SOG.Polycurve> polycurveConverter,
    ITypedConverter<RG.Polyline, SOG.Polyline> polylineConverter,
    ITypedConverter<RG.Mesh, SOG.Mesh> meshConverter,
    ITypedConverter<RG.Brep, SOG.BrepX> brepConverter,
    ITypedConverter<RG.Extrusion, SOG.ExtrusionX> extrusionConverter,
    ITypedConverter<RG.SubD, SOG.SubDX> subdConverter
  )
  {
    _pointConverter = pointConverter;
    _arcCurveConverter = arcCurveConverter;
    _hatchConverter = hatchConverter;
    _lineCurveConverter = lineCurveConverter;
    _nurbsCurveConverter = nurbsCurveConverter;
    _pointcloudConverter = pointcloudConverter;
    _polycurveConverter = polycurveConverter;
    _polylineConverter = polylineConverter;
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
      RG.LineCurve ln => _lineCurveConverter.Convert(ln),
      RG.NurbsCurve nurbsCurve => _nurbsCurveConverter.Convert(nurbsCurve),
      RG.PointCloud pointcloud => _pointcloudConverter.Convert(pointcloud),
      RG.PolyCurve polyCurve => _polycurveConverter.Convert(polyCurve),
      RG.Polyline polyline => _polylineConverter.Convert(polyline),
      RG.PolylineCurve polylineCurve => _polylineConverter.Convert(polylineCurve.ToPolyline()),
      RG.Mesh mesh => _meshConverter.Convert(mesh),
      RG.Brep brep => _brepConverter.Convert(brep),
      RG.Extrusion ext => _extrusionConverter.Convert(ext),
      RG.SubD subD => _subdConverter.Convert(subD),
      _ => throw new ConversionException($"Failed to find a conversion for {target.GetType()}")
    };
  }
}
