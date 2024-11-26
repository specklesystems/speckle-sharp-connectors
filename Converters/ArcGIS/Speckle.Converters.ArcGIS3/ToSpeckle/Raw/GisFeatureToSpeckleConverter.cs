using ArcGIS.Core.Data;
using Speckle.Converters.ArcGIS3.Utils;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.Data;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.ArcGIS3.ToSpeckle.Raw;

public class GisFeatureToSpeckleConverter : ITypedConverter<(Row, string), GisObject>
{
  private readonly ITypedConverter<ACG.MapPoint, SOG.Point> _pointConverter;
  private readonly ITypedConverter<ACG.Multipoint, IReadOnlyList<SOG.Point>> _multiPointConverter;
  private readonly ITypedConverter<ACG.Polyline, IReadOnlyList<SOG.Polyline>> _polylineConverter;
  private readonly ITypedConverter<ACG.Polygon, IReadOnlyList<SGIS.PolygonGeometry>> _polygonConverter;
  private readonly ITypedConverter<ACG.Multipatch, IReadOnlyList<Base>> _multipatchConverter;
  private readonly IConverterSettingsStore<ArcGISConversionSettings> _settingsStore;

  public GisFeatureToSpeckleConverter(
    ITypedConverter<ACG.MapPoint, SOG.Point> pointConverter,
    ITypedConverter<ACG.Multipoint, IReadOnlyList<SOG.Point>> multiPointConverter,
    ITypedConverter<ACG.Polyline, IReadOnlyList<SOG.Polyline>> polylineConverter,
    ITypedConverter<ACG.Polygon, IReadOnlyList<SGIS.PolygonGeometry>> polygonConverter,
    ITypedConverter<ACG.Multipatch, IReadOnlyList<Base>> multipatchConverter,
    IConverterSettingsStore<ArcGISConversionSettings> settingsStore
  )
  {
    _pointConverter = pointConverter;
    _multiPointConverter = multiPointConverter;
    _polylineConverter = polylineConverter;
    _polygonConverter = polygonConverter;
    _multipatchConverter = multipatchConverter;
    _settingsStore = settingsStore;
  }

  private List<SOG.Mesh> GetPolygonDisplayMeshes(List<SGIS.PolygonGeometry> polygons)
  {
    List<SOG.Mesh> displayVal = new();
    foreach (SGIS.PolygonGeometry polygon in polygons)
    {
      // POC: check for voids, we cannot generate display value correctly if any of the polygons have voids
      // Return meshed boundary for now, ignore voids
      // if (polygon.voids.Count > 0)
      // {
      //   return new();
      // }

      // ensure counter-clockwise orientation for up-facing mesh faces
      bool isClockwise = polygon.boundary.IsClockwisePolygon();
      List<SOG.Point> boundaryPts = polygon.boundary.GetPoints();
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

    return displayVal;
  }

  private List<SOG.Mesh> GetMultipatchDisplayMeshes(List<SGIS.GisMultipatchGeometry> multipatch)
  {
    List<SOG.Mesh> displayVal = new();
    foreach (SGIS.GisMultipatchGeometry geo in multipatch)
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
    List<SGIS.PolygonGeometry> polygons = new();
    List<SGIS.GisMultipatchGeometry> multipatches = new();
    foreach (Base geo in geometry)
    {
      if (geo is SGIS.GisMultipatchGeometry multipatch)
      {
        multipatches.Add(multipatch);
      }
      else if (geo is SGIS.PolygonGeometry polygon)
      {
        polygons.Add(polygon);
      }
    }

    displayValue.AddRange(GetPolygonDisplayMeshes(polygons));
    displayValue.AddRange(GetMultipatchDisplayMeshes(multipatches));
    return displayValue;
  }

  public GisObject Convert((Row, string) target)
  {
    Row row = target.Item1;
    string appId = target.Item2;

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
        applicationId = appId,
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
          type = "Point",
          name = "Point Feature",
          applicationId = appId,
          displayValue = new List<Base>() { specklePoint },
        };

      case ACG.Multipoint multipoint:
        List<SOG.Point> specklePoints = _multiPointConverter.Convert(multipoint).ToList();
        return new GisObject()
        {
          type = "Point",
          name = "Point Feature",
          applicationId = appId,
          displayValue = specklePoints,
        };

      case ACG.Polyline polyline:
        List<SOG.Polyline> polylines = _polylineConverter.Convert(polyline).ToList();
        return new GisObject()
        {
          type = "Line",
          name = "Line Feature",
          applicationId = appId,
          displayValue = polylines,
        };

      case ACG.Polygon polygon:
        List<SGIS.PolygonGeometry> polygons = _polygonConverter.Convert(polygon).ToList();
        List<SOG.Mesh> meshes = GetPolygonDisplayMeshes(polygons);
        return new GisObject()
        {
          type = "Polygon",
          name = "Polygon Feature",
          applicationId = appId,
          displayValue = meshes,
        };

      case ACG.Multipatch multipatch:
        List<Base> geometry = _multipatchConverter.Convert(multipatch).ToList();
        List<SOG.Mesh> display = GetDisplayMeshes(geometry);
        return new GisObject()
        {
          type = "Multipatch",
          name = "Multipatch Feature",
          applicationId = appId,
          displayValue = display,
        };

      default:
        throw new ValidationException($"No geometry conversion found for {shape.GetType().Name}");
    }
  }
}
