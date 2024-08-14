using ArcGIS.Core.Data;
using Speckle.Converters.ArcGIS3.Utils;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects;
using Speckle.Objects.Geometry;
using Speckle.Objects.GIS;
using Speckle.Sdk.Models;

namespace Speckle.Converters.ArcGIS3.ToSpeckle.Raw;

public class GisFeatureToSpeckleConverter : ITypedConverter<Row, IGisFeature>
{
  private readonly ITypedConverter<ACG.MapPoint, SOG.Point> _pointConverter;
  private readonly ITypedConverter<ACG.Multipoint, IReadOnlyList<SOG.Point>> _multiPointConverter;
  private readonly ITypedConverter<ACG.Polyline, IReadOnlyList<SOG.Polyline>> _polylineConverter;
  private readonly ITypedConverter<ACG.Polygon, IReadOnlyList<PolygonGeometry>> _polygonConverter;
  private readonly ITypedConverter<ACG.Multipatch, IReadOnlyList<Base>> _multipatchConverter;
  private readonly ITypedConverter<Row, Base> _attributeConverter;

  public GisFeatureToSpeckleConverter(
    ITypedConverter<ACG.MapPoint, SOG.Point> pointConverter,
    ITypedConverter<ACG.Multipoint, IReadOnlyList<SOG.Point>> multiPointConverter,
    ITypedConverter<ACG.Polyline, IReadOnlyList<SOG.Polyline>> polylineConverter,
    ITypedConverter<ACG.Polygon, IReadOnlyList<PolygonGeometry>> polygonConverter,
    ITypedConverter<ACG.Multipatch, IReadOnlyList<Base>> multipatchConverter,
    ITypedConverter<Row, Base> attributeConverter
  )
  {
    _pointConverter = pointConverter;
    _multiPointConverter = multiPointConverter;
    _polylineConverter = polylineConverter;
    _polygonConverter = polygonConverter;
    _multipatchConverter = multipatchConverter;
    _attributeConverter = attributeConverter;
  }

  private List<Mesh> GetPolygonDisplayMeshes(List<SGIS.PolygonGeometry> polygons)
  {
    List<Mesh> displayVal = new();
    foreach (SGIS.PolygonGeometry polygon in polygons)
    {
      try
      {
        if (polygon.voids.Count == 0)
        {
          // ensure counter-clockwise orientation for up-facing mesh faces
          bool isClockwise = polygon.boundary.IsClockwisePolygon();
          List<SOG.Point> boundaryPts = polygon.boundary.GetPoints();
          if (isClockwise)
          {
            boundaryPts.Reverse();
          }

          // generate Mesh
          int ptCount = boundaryPts.Count;
          List<int> faces = new() { ptCount };
          faces.AddRange(Enumerable.Range(0, ptCount).ToList());

          SOG.Mesh mesh = new(boundaryPts.SelectMany(x => new List<double> { x.x, x.y, x.z }).ToList(), faces);
          displayVal.Add(mesh);
        }
        else
        {
          throw new SpeckleConversionException("Cannot generate display value for polygons with voids");
        }
      }
      catch (SpeckleConversionException)
      {
        break;
      }
    }
    return displayVal;
  }

  private List<Mesh> GetMultipatchDisplayMeshes(List<SGIS.GisMultipatchGeometry> multipatch)
  {
    List<Mesh> displayVal = new();
    foreach (GisMultipatchGeometry geo in multipatch)
    {
      SOG.Mesh displayMesh = new(geo.vertices, geo.faces);
      displayVal.Add(displayMesh);
    }

    return displayVal;
  }

  private List<Mesh> GetDisplayMeshes(List<Base> geometry)
  {
    List<Mesh> displayValue = new();
    List<SGIS.PolygonGeometry> polygons = new();
    List<SGIS.GisMultipatchGeometry> multipatches = new();
    foreach (Base geo in geometry)
    {
      if (geo is GisMultipatchGeometry multipatch)
      {
        multipatches.Add(multipatch);
      }
      else if (geo is PolygonGeometry polygon)
      {
        polygons.Add(polygon);
      }
    }

    displayValue.AddRange(GetPolygonDisplayMeshes(polygons));
    displayValue.AddRange(GetMultipatchDisplayMeshes(multipatches));
    return displayValue;
  }

  public IGisFeature Convert(Row target)
  {
    // get attributes
    Base attributes = _attributeConverter.Convert(target);

    bool hasGeometry = false;
    string geometryField = "Shape";
    foreach (Field field in target.GetFields())
    {
      // POC: check for all possible reserved Shape names
      if (field.FieldType == FieldType.Geometry) // ignore the field with geometry itself
      {
        hasGeometry = true;
        geometryField = field.Name;
      }
    }

    // return GisFeatures that don't have geometry
    if (!hasGeometry)
    {
      return new SGIS.GisNonGeometricFeature(attributes);
    }

    var shape = (ACG.Geometry)target[geometryField];
    switch (shape)
    {
      case ACG.MapPoint point:
        Point specklePoint = _pointConverter.Convert(point);
        return new SGIS.GisPointFeature(new() { specklePoint }, attributes);

      case ACG.Multipoint multipoint:
        List<Point> specklePoints = _multiPointConverter.Convert(multipoint).ToList();
        return new SGIS.GisPointFeature(specklePoints, attributes);

      case ACG.Polyline polyline:
        List<Polyline> polylines = _polylineConverter.Convert(polyline).ToList();
        return new SGIS.GisPolylineFeature(polylines, attributes);

      case ACG.Polygon polygon:
        List<PolygonGeometry> polygons = _polygonConverter.Convert(polygon).ToList();
        List<Mesh> meshes = GetPolygonDisplayMeshes(polygons);
        return new SGIS.GisPolygonFeature(polygons, meshes, attributes);

      case ACG.Multipatch multipatch:
        List<Base> geometry = _multipatchConverter.Convert(multipatch).ToList();
        List<Mesh> display = GetDisplayMeshes(geometry);
        return new SGIS.GisMultipatchFeature(geometry, display, attributes);

      default:
        throw new NotSupportedException($"No geometry conversion found for {shape.GetType().Name}");
    }
  }
}
