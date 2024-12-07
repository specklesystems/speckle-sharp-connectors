using ArcGIS.Core.Data;
using ArcGIS.Core.Data.Raster;
using ArcGIS.Desktop.Mapping;
using Speckle.Converters.ArcGIS3.Utils;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.Data;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.ArcGIS3.ToSpeckle.TopLevel;

[NameAndRankValue(nameof(AC.CoreObjectsBase), 0)]
public class CoreObjectsBaseToSpeckleTopLevelConverter : IToSpeckleTopLevelConverter
{
  private readonly ITypedConverter<ACG.MapPoint, SOG.Point> _pointConverter;
  private readonly ITypedConverter<ACG.Multipoint, IReadOnlyList<SOG.Point>> _multiPointConverter;
  private readonly ITypedConverter<ACG.Polyline, IReadOnlyList<SOG.Polyline>> _polylineConverter;
  private readonly ITypedConverter<ACG.Polygon, IReadOnlyList<SOG.Polygon>> _polygonConverter;
  private readonly ITypedConverter<ACG.Multipatch, IReadOnlyList<Base>> _multipatchConverter;
  private readonly ITypedConverter<Raster, SOG.Mesh> _gisRasterConverter;
  private readonly ITypedConverter<LasDatasetLayer, Speckle.Objects.Geometry.Pointcloud> _pointcloudConverter;
  private readonly IConverterSettingsStore<ArcGISConversionSettings> _settingsStore;

  public CoreObjectsBaseToSpeckleTopLevelConverter(
    ITypedConverter<ACG.MapPoint, SOG.Point> pointConverter,
    ITypedConverter<ACG.Multipoint, IReadOnlyList<SOG.Point>> multiPointConverter,
    ITypedConverter<ACG.Polyline, IReadOnlyList<SOG.Polyline>> polylineConverter,
    ITypedConverter<ACG.Polygon, IReadOnlyList<SOG.Polygon>> polygonConverter,
    ITypedConverter<ACG.Multipatch, IReadOnlyList<Base>> multipatchConverter,
    ITypedConverter<Raster, SOG.Mesh> gisRasterConverter,
    ITypedConverter<LasDatasetLayer, Speckle.Objects.Geometry.Pointcloud> pointcloudConverter,
    IConverterSettingsStore<ArcGISConversionSettings> settingsStore
  )
  {
    _pointConverter = pointConverter;
    _multiPointConverter = multiPointConverter;
    _polylineConverter = polylineConverter;
    _polygonConverter = polygonConverter;
    _multipatchConverter = multipatchConverter;
    _gisRasterConverter = gisRasterConverter;
    _pointcloudConverter = pointcloudConverter;
    _settingsStore = settingsStore;
  }

  public Arcgisobject Convert(object target)
  {
    if (target is Row row)
    {
      bool hasGeometry = false;
      string geometryField = "Shape"; // placeholder, assigned in the loop below
      foreach (Field field in row.GetFields())
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
        return new GisObject()
        {
          type = "", // no geometry
          name = "Table Row",
          applicationId = "",
          displayValue = new List<Base>()
        };
      }

      var shape = (ACG.Geometry)row[geometryField];
      switch (shape)
      {
        case ACG.MapPoint point:
          SOG.Point specklePoint = _pointConverter.Convert(point);
          return new GisObject()
          {
            type = GISLayerGeometryType.POINT,
            name = "Point Feature",
            applicationId = "",
            displayValue = new List<Base>() { specklePoint },
          };

        case ACG.Multipoint multipoint:
          List<SOG.Point> specklePoints = _multiPointConverter.Convert(multipoint).ToList();
          return new GisObject()
          {
            type = GISLayerGeometryType.POINT,
            name = "Point Feature",
            applicationId = "",
            displayValue = specklePoints,
          };

        case ACG.Polyline polyline:
          List<SOG.Polyline> polylines = _polylineConverter.Convert(polyline).ToList();
          return new GisObject()
          {
            type = GISLayerGeometryType.POLYLINE,
            name = "Line Feature",
            applicationId = "",
            displayValue = polylines,
          };

        case ACG.Polygon polygon:
          List<SOG.Polygon> polygons = _polygonConverter.Convert(polygon).ToList();
          List<SOG.Mesh> meshes = GetPolygonDisplayMeshes(polygons);
          GisObject gisObj =
            new()
            {
              type = GISLayerGeometryType.POLYGON,
              name = "Polygon Feature",
              applicationId = "",
              displayValue = meshes,
            };
          gisObj["geometry"] = polygons;
          return gisObj;

        case ACG.Multipatch multipatch:
          List<Base> geometry = _multipatchConverter.Convert(multipatch).ToList();
          List<SOG.Mesh> display = GetDisplayMeshes(geometry);
          return new GisObject()
          {
            type = GISLayerGeometryType.MULTIPATCH,
            name = "Multipatch Feature",
            applicationId = "",
            displayValue = display,
          };

        default:
          throw new ValidationException($"No geometry conversion found for {shape.GetType().Name}");
      }
    }

    if (target is Raster raster)
    {
      SOG.Mesh displayMesh = _gisRasterConverter.Convert(raster);
      return new GisObject()
      {
        type = GISLayerGeometryType.RASTER,
        name = "Raster",
        applicationId = "",
        displayValue = new List<Base>() { displayMesh },
      };
    }

    if (target is LasDatasetLayer pointcloudLayer)
    {
      Speckle.Objects.Geometry.Pointcloud cloud = _pointcloudConverter.Convert(pointcloudLayer);
      return new GisObject()
      {
        type = GISLayerGeometryType.POINTCLOUD,
        name = "Pointcloud",
        applicationId = "",
        displayValue = new List<Base>() { cloud },
      };
    }

    throw new NotImplementedException($"Conversion of object type {target.GetType()} is not supported");
  }

  private List<SOG.Mesh> GetPolygonDisplayMeshes(List<SOG.Polygon> polygons)
  {
    List<SOG.Mesh> displayVal = new();
    foreach (SOG.Polygon polygon in polygons)
    {
      // POC: check for voids, we cannot generate display value correctly if any of the polygons have voids
      // Return meshed boundary for now, ignore voids
      // if (polygon.voids.Count > 0)
      // {
      //   return new();
      // }

      // ensure counter-clockwise orientation for up-facing mesh faces
      // TODO: implement for ICurves too
      if (polygon.boundary is SOG.Polyline boundaryPolyline)
      {
        bool isClockwise = boundaryPolyline.IsClockwisePolygon();
        List<SOG.Point> boundaryPts = boundaryPolyline.GetPoints();
        if (isClockwise)
        {
          boundaryPts.Reverse();
        }

        // generate Mesh
        List<int> faces = new() { boundaryPts.Count };
        faces.AddRange(Enumerable.Range(0, boundaryPts.Count).ToList());
        SOG.Mesh mesh =
          new()
          {
            vertices = boundaryPts.SelectMany(x => new List<double> { x.x, x.y, x.z }).ToList(),
            faces = faces,
            units = _settingsStore.Current.SpeckleUnits
          };
        displayVal.Add(mesh);
      }
    }

    return displayVal;
  }

  private List<SOG.Mesh> GetMultipatchDisplayMeshes(List<SOG.Mesh> multipatch)
  {
    List<SOG.Mesh> displayVal = new();
    foreach (SOG.Mesh geo in multipatch)
    {
      SOG.Mesh displayMesh =
        new()
        {
          vertices = geo.vertices,
          faces = geo.faces,
          units = _settingsStore.Current.SpeckleUnits
        };
      displayVal.Add(displayMesh);
    }

    return displayVal;
  }

  private List<SOG.Mesh> GetDisplayMeshes(List<Base> geometry)
  {
    List<SOG.Mesh> displayValue = new();
    List<SOG.Polygon> polygons = new();
    List<SOG.Mesh> multipatches = new();
    foreach (Base geo in geometry)
    {
      if (geo is SOG.Mesh multipatch)
      {
        multipatches.Add(multipatch);
      }
      else if (geo is SOG.Polygon polygon)
      {
        polygons.Add(polygon);
      }
    }

    displayValue.AddRange(GetPolygonDisplayMeshes(polygons));
    displayValue.AddRange(GetMultipatchDisplayMeshes(multipatches));
    return displayValue;
  }
}
