using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.Data;
using Speckle.Sdk.Common;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Rhino.ToHost.TopLevel;

[NameAndRankValue(typeof(DataObject), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class DataObjectConverter
  : IToHostTopLevelConverter,
    ITypedConverter<DataObject, List<(RG.GeometryBase a, Base b)>>
{
  private readonly ITypedConverter<SOG.Arc, RG.ArcCurve> _arcConverter;
  private readonly ITypedConverter<SOG.BrepX, List<RG.GeometryBase>> _brepConverter;
  private readonly ITypedConverter<SOG.Circle, RG.ArcCurve> _circleConverter;
  private readonly ITypedConverter<SOG.Curve, RG.NurbsCurve> _curveConverter;
  private readonly ITypedConverter<SOG.Ellipse, RG.NurbsCurve> _ellipseConverter;
  private readonly ITypedConverter<SOG.ExtrusionX, List<RG.GeometryBase>> _extrusionConverter;
  private readonly ITypedConverter<SOG.Line, RG.LineCurve> _lineConverter;
  private readonly ITypedConverter<SOG.Mesh, RG.Mesh> _meshConverter;
  private readonly ITypedConverter<SOG.Pointcloud, RG.PointCloud> _pointcloudConverter;
  private readonly ITypedConverter<SOG.Point, RG.Point> _pointConverter;
  private readonly ITypedConverter<SOG.Polycurve, RG.PolyCurve> _polycurveConverter;
  private readonly ITypedConverter<SOG.Polyline, RG.PolylineCurve> _polylineConverter;
  private readonly ITypedConverter<SOG.Region, RG.Hatch> _regionConverter;
  private readonly ITypedConverter<SOG.SubDX, List<RG.GeometryBase>> _subdConverter;
  private readonly IConverterSettingsStore<RhinoConversionSettings> _settingsStore;

  public DataObjectConverter(
    ITypedConverter<SOG.Arc, RG.ArcCurve> arcConverter,
    ITypedConverter<SOG.BrepX, List<RG.GeometryBase>> brepConverter,
    ITypedConverter<SOG.Circle, RG.ArcCurve> circleConverter,
    ITypedConverter<SOG.Curve, RG.NurbsCurve> curveConverter,
    ITypedConverter<SOG.Ellipse, RG.NurbsCurve> ellipseConverter,
    ITypedConverter<SOG.ExtrusionX, List<RG.GeometryBase>> extrusionConverter,
    ITypedConverter<SOG.Line, RG.LineCurve> lineConverter,
    ITypedConverter<SOG.Mesh, RG.Mesh> meshConverter,
    ITypedConverter<SOG.Pointcloud, RG.PointCloud> pointcloudConverter,
    ITypedConverter<SOG.Point, RG.Point> pointConverter,
    ITypedConverter<SOG.Polyline, RG.PolylineCurve> polylineConverter,
    ITypedConverter<SOG.Polycurve, RG.PolyCurve> polycurveConverter,
    ITypedConverter<SOG.Region, RG.Hatch> regionConverter,
    ITypedConverter<SOG.SubDX, List<RG.GeometryBase>> subdConverter,
    IConverterSettingsStore<RhinoConversionSettings> settingsStore
  )
  {
    _arcConverter = arcConverter;
    _brepConverter = brepConverter;
    _circleConverter = circleConverter;
    _curveConverter = curveConverter;
    _ellipseConverter = ellipseConverter;
    _extrusionConverter = extrusionConverter;
    _lineConverter = lineConverter;
    _meshConverter = meshConverter;
    _pointcloudConverter = pointcloudConverter;
    _pointConverter = pointConverter;
    _polycurveConverter = polycurveConverter;
    _polylineConverter = polylineConverter;
    _regionConverter = regionConverter;
    _subdConverter = subdConverter;
    _settingsStore = settingsStore;
  }

  public object Convert(Base target) => Convert((DataObject)target);

  private List<RG.GeometryBase> GetConvertedGeometry(Base b)
  {
    return b switch
    {
      SOG.Arc arc => new() { _arcConverter.Convert(arc) },
      SOG.BrepX brep => _brepConverter.Convert(brep),
      SOG.Circle circle => new() { _circleConverter.Convert(circle) },
      SOG.Curve curve => new() { _curveConverter.Convert(curve) },
      SOG.Ellipse ellipse => new() { _ellipseConverter.Convert(ellipse) },
      SOG.ExtrusionX extrusion => _extrusionConverter.Convert(extrusion),
      SOG.Line line => new() { _lineConverter.Convert(line) },
      SOG.Mesh mesh => new() { _meshConverter.Convert(mesh) },
      SOG.Pointcloud pointcloud => new() { _pointcloudConverter.Convert(pointcloud) },
      SOG.Point point => new() { _pointConverter.Convert(point) },
      SOG.Polycurve polycurve => new() { _polycurveConverter.Convert(polycurve) },
      SOG.Polyline polyline => new() { _polylineConverter.Convert(polyline) },
      SOG.Region region => new() { _regionConverter.Convert(region) },
      SOG.SubDX subd => _subdConverter.Convert(subd),
      _ => throw new ConversionException($"Found unsupported fallback geometry: {b.GetType()}")
    };
  }

  public List<(RG.GeometryBase a, Base b)> Convert(DataObject target)
  {
    var result = new List<RG.GeometryBase>();
    foreach (var item in target.displayValue)
    {
      var converted = GetConvertedGeometry(item);
      foreach (var x in converted)
      {
        x.Transform(GetUnitsTransform(item));
        result.Add(x);
      }
    }

    return result.Zip(target.displayValue, (a, b) => (a, b)).ToList();
  }

  private RG.Transform GetUnitsTransform(Base speckleObject)
  {
    /*
     * POC: CNX-9270 Looking at a simpler, more performant way of doing unit scaling on `ToNative`
     * by fully relying on the transform capabilities of the HostApp, and only transforming top-level stuff.
     * This may not hold when adding more complex conversions, but it works for now!
     */
    if (speckleObject["units"] is string units)
    {
      var scaleFactor = Units.GetConversionFactor(units, _settingsStore.Current.SpeckleUnits);
      var scale = RG.Transform.Scale(RG.Point3d.Origin, scaleFactor);
      return scale;
    }

    return RG.Transform.Identity;
  }
}
