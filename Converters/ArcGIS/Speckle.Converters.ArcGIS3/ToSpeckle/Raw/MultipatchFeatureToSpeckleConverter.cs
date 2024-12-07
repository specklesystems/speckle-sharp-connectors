using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using ValidationException = System.ComponentModel.DataAnnotations.ValidationException;

namespace Speckle.Converters.ArcGIS3.ToSpeckle.Raw;

/// <summary>
/// Converts Multipatch objects into Meshes
/// </summary>
public class MultipatchFeatureToSpeckleConverter : ITypedConverter<ACG.Multipatch, IReadOnlyList<SOG.Mesh>>
{
  private readonly IConverterSettingsStore<ArcGISConversionSettings> _settingsStore;
  private readonly ITypedConverter<ACG.MapPoint, SOG.Point> _pointConverter;

  public MultipatchFeatureToSpeckleConverter(
    IConverterSettingsStore<ArcGISConversionSettings> settingsStore,
    ITypedConverter<ACG.MapPoint, SOG.Point> pointConverter
  )
  {
    _settingsStore = settingsStore;
    _pointConverter = pointConverter;
  }

  public IReadOnlyList<SOG.Mesh> Convert(ACG.Multipatch target)
  {
    List<SOG.Mesh> converted = new();
    // placeholder, needs to be declared in order to be used in the Ring patch type
    //SOG.Polygon polygonGeom = new() { units = _settingsStore.Current.SpeckleUnits };

    // convert and store all multipatch points per Part
    List<List<SOG.Point>> allPoints = new();
    for (int idx = 0; idx < target.PartCount; idx++)
    {
      List<SOG.Point> pointList = new();
      int ptStartIndex = target.GetPatchStartPointIndex(idx);
      int ptCount = target.GetPatchPointCount(idx);
      for (int ptIdx = ptStartIndex; ptIdx < ptStartIndex + ptCount; ptIdx++)
      {
        pointList.Add(_pointConverter.Convert(target.Points[ptIdx]));
      }
      allPoints.Add(pointList);
    }

    // convert all parts
    for (int i = 0; i < target.PartCount; i++)
    {
      // get the patch type to get the point arrangement
      // https://pro.arcgis.com/en/pro-app/latest/sdk/api-reference/topic27403.html
      ACG.PatchType patchType = target.GetPatchType(i);

      // get the points in the patch
      List<ACG.MapPoint> points = new();
      int ptStartIndex = target.GetPatchStartPointIndex(i);
      for (int ptIdx = ptStartIndex; ptIdx < ptStartIndex + target.GetPatchPointCount(i); ptIdx++)
      {
        points.Add(target.Points[ptIdx]);
      }

      switch (patchType)
      {
        case ACG.PatchType.TriangleStrip:
          SOG.Mesh triangleStripPatch = GetMeshFromTriangleStripPatch(points);
          converted.Add(triangleStripPatch);
          break;
        case ACG.PatchType.Triangles:
          SOG.Mesh trianglesPatch = GetMeshFromTrianglesPatch(points);
          converted.Add(trianglesPatch);
          break;
        case ACG.PatchType.TriangleFan:
          SOG.Mesh triangleFanPatch = GetMeshFromTriangleFanPatch(points);
          converted.Add(triangleFanPatch);
          break;
        case ACG.PatchType.FirstRing:
          SOG.Mesh firstRingPatch = GetMeshFromFirstRingPatch(points);
          converted.Add(firstRingPatch);
          break;

        default:
          throw new ValidationException($"{patchType} patch type is not supported");
      }
    }
    return converted;
  }

  private SOG.Mesh GetMeshFromTriangleStripPatch(List<ACG.MapPoint> points)
  {
    List<double> pointCoords = points.SelectMany(x => new List<double>() { x.X, x.Y, x.Z }).ToList();
    List<int> faces = new();
    List<double> vertices = new();

    for (int i = 0; i < points.Count; i++)
    {
      if (i >= 2) // every new point adds a triangle
      {
        faces.AddRange(new List<int>() { 3, i - 2, i - 1, i });
        vertices.AddRange(pointCoords.GetRange(3 * (i - 2), 9).ToList());
      }
    }

    return new()
    {
      faces = faces,
      vertices = vertices,
      units = _settingsStore.Current.SpeckleUnits
    };
  }

  private SOG.Mesh GetMeshFromTrianglesPatch(List<ACG.MapPoint> points)
  {
    List<double> pointCoords = points.SelectMany(x => new List<double>() { x.X, x.Y, x.Z }).ToList();
    List<int> faces = new();
    List<double> vertices = new();

    for (int i = 0; i < points.Count; i++)
    {
      if (i >= 2 && (i + 1) % 3 == 0) // every 3 new points is a new triangle
      {
        faces.AddRange(new List<int>() { 3, i - 2, i - 1, i });
        vertices.AddRange(pointCoords.GetRange(3 * (i - 2), 9).ToList());
      }
    }

    return new()
    {
      faces = faces,
      vertices = vertices,
      units = _settingsStore.Current.SpeckleUnits
    };
  }

  private SOG.Mesh GetMeshFromTriangleFanPatch(List<ACG.MapPoint> points)
  {
    List<double> pointCoords = points.SelectMany(x => new List<double>() { x.X, x.Y, x.Z }).ToList();
    List<int> faces = new();
    List<double> vertices = new();

    for (int i = 0; i < points.Count; i++)
    {
      if (i >= 2) // every new point adds a triangle (originates from 0)
      {
        faces.AddRange(new List<int>() { 3, 0, i - 1, i });
        vertices.AddRange(pointCoords.GetRange(2 * (i - 2), 6).ToList());
      }
    }

    return new()
    {
      faces = faces,
      vertices = vertices,
      units = _settingsStore.Current.SpeckleUnits
    };
  }

  // first ring means a start of a new PolygonGeometry3d
  // POC: guess we are skipping inner rings for now, though we could send as polylines
  private SOG.Mesh GetMeshFromFirstRingPatch(List<ACG.MapPoint> points)
  {
    List<double> pointCoords = points.SelectMany(x => new List<double>() { x.X, x.Y, x.Z }).ToList();
    List<int> faces = Enumerable.Range(0, pointCoords.Count).ToList();

    return new()
    {
      faces = faces,
      vertices = pointCoords,
      units = _settingsStore.Current.SpeckleUnits
    };
  }
}
