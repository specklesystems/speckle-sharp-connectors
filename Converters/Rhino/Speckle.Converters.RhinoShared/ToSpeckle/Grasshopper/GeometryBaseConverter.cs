using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Rhino.ToSpeckle.Grasshopper;

[NameAndRankValue(nameof(RG.GeometryBase), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class GeometryBaseConverter : IToSpeckleTopLevelConverter
{
  private readonly ITypedConverter<RG.Point, SOG.Point> _pointConverter;
  private readonly ITypedConverter<RG.ArcCurve, Base> _arcCurveConverter;
  private readonly ITypedConverter<RG.LineCurve, SOG.Line> _lineCurveConverter;
  private readonly ITypedConverter<RG.NurbsCurve, SOG.Curve> _nurbsCurveConverter;
  private readonly ITypedConverter<RG.PolyCurve, SOG.Polycurve> _polycurveConverter;
  private readonly ITypedConverter<RG.Polyline, SOG.Polyline> _polylineConverter;
  private readonly ITypedConverter<RG.Mesh, SOG.Mesh> _meshConverter;
  private readonly ITypedConverter<RG.Extrusion, SOG.ExtrusionX> _extrusionConverter;
  private readonly ITypedConverter<RG.SubD, SOG.SubDX> _subdConverter;
  private readonly ITypedConverter<RG.Brep, SOG.BrepX> _brepConverter;

  public GeometryBaseConverter(
    ITypedConverter<RG.Point, SOG.Point> pointConverter,
    ITypedConverter<RG.ArcCurve, Base> arcCurveConverter,
    ITypedConverter<RG.LineCurve, SOG.Line> lineCurveConverter,
    ITypedConverter<RG.NurbsCurve, SOG.Curve> nurbsCurveConverter,
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
    _lineCurveConverter = lineCurveConverter;
    _nurbsCurveConverter = nurbsCurveConverter;
    _polycurveConverter = polycurveConverter;
    _polylineConverter = polylineConverter;
    _meshConverter = meshConverter;
    _brepConverter = brepConverter;
    _extrusionConverter = extrusionConverter;
    _subdConverter = subdConverter;
  }

  public Base Convert(object target)
  {
    switch (target)
    {
      case RG.Point pt:
        return _pointConverter.Convert(pt);
      case RG.ArcCurve ac:
        return _arcCurveConverter.Convert(ac);
      case RG.LineCurve ln:
        return _lineCurveConverter.Convert(ln);
      case RG.NurbsCurve nurbsCurve:
        return _nurbsCurveConverter.Convert(nurbsCurve);
      case RG.PolyCurve polyCurve:
        return _polycurveConverter.Convert(polyCurve);
      case RG.Polyline polyline:
        return _polylineConverter.Convert(polyline);
      case RG.PolylineCurve polylineCurve:
        return _polylineConverter.Convert(polylineCurve.ToPolyline());
      case RG.Mesh mesh:
        return _meshConverter.Convert(mesh);
      case RG.Brep brep:
        return _brepConverter.Convert(brep);
      case RG.Extrusion ext:
        return _extrusionConverter.Convert(ext);
      case RG.SubD subD:
        return _subdConverter.Convert(subD);
    }

    throw new ConversionException($"Failed to find a conversion for {target.GetType()}");
  }
}
