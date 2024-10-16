using Speckle.Converters.ArcGIS3.Utils;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;
using ValidationException = System.ComponentModel.DataAnnotations.ValidationException;

namespace Speckle.Converters.ArcGIS3.ToSpeckle.Raw;

/// <summary>
/// Converts Multipatch objects into a list containing some combination of GisMultipatchGeometry or PolygonGeometry3d objects
/// </summary>
public class MultipatchFeatureToSpeckleConverter : ITypedConverter<ACG.Multipatch, IReadOnlyList<Base>>
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

  public IReadOnlyList<Base> Convert(ACG.Multipatch target)
  {
    List<Base> converted = new();
    // placeholder, needs to be declared in order to be used in the Ring patch type
    SGIS.PolygonGeometry3d polygonGeom = new() { units = _settingsStore.Current.SpeckleUnits };

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

    for (int idx = 0; idx < target.PartCount; idx++)
    {
      // get the patch type to get the point arrangement in the mesh
      // https://pro.arcgis.com/en/pro-app/latest/sdk/api-reference/topic27403.html
      ACG.PatchType patchType = target.GetPatchType(idx);
      int ptCount = target.GetPatchPointCount(idx);

      if (patchType == ACG.PatchType.TriangleStrip)
      {
        SGIS.GisMultipatchGeometry multipatch = target.CompleteMultipatchTriangleStrip(allPoints, idx);
        multipatch.units = _settingsStore.Current.SpeckleUnits;
        converted.Add(multipatch);
      }
      else if (patchType == ACG.PatchType.Triangles)
      {
        SGIS.GisMultipatchGeometry multipatch = target.CompleteMultipatchTriangles(allPoints, idx);
        multipatch.units = _settingsStore.Current.SpeckleUnits;
        converted.Add(multipatch);
      }
      else if (patchType == ACG.PatchType.TriangleFan)
      {
        SGIS.GisMultipatchGeometry multipatch = target.CompleteMultipatchTriangleFan(allPoints, idx);
        multipatch.units = _settingsStore.Current.SpeckleUnits;
        converted.Add(multipatch);
      }
      // in case of RingMultipatch - return PolygonGeometry3d
      // the following Patch Parts cannot be pushed to external method, as they will possibly, add voids/rings to the same GisPolygon
      else if (patchType == ACG.PatchType.FirstRing)
      {
        // chech if there were already Polygons, add them to list
        if (polygonGeom.boundary != null)
        {
          converted.Add(polygonGeom);
        }

        // first ring means a start of a new PolygonGeometry3d
        polygonGeom = new() { voids = new List<SOG.Polyline>(), units = _settingsStore.Current.SpeckleUnits };
        List<double> pointCoords = allPoints[idx].SelectMany(x => new List<double>() { x.x, x.y, x.z }).ToList();

        SOG.Polyline polyline = new() { value = pointCoords, units = _settingsStore.Current.SpeckleUnits };
        polygonGeom.boundary = polyline;

        // if it's already the last part, add to list
        if (idx == target.PartCount - 1)
        {
          converted.Add(polygonGeom);
        }
      }
      else if (patchType == ACG.PatchType.Ring)
      {
        List<double> pointCoords = allPoints[idx].SelectMany(x => new List<double>() { x.x, x.y, x.z }).ToList();
        SOG.Polyline polyline = new() { value = pointCoords, units = _settingsStore.Current.SpeckleUnits };

        // every outer ring is oriented clockwise
        bool isClockwise = polyline.IsClockwisePolygon();
        if (!isClockwise)
        {
          // add void to existing polygon
          polygonGeom.voids.Add(polyline);
        }
        else
        {
          // add existing polygon to list, start a new polygon with a boundary
          converted.Add(polygonGeom);
          polygonGeom = new()
          {
            voids = new List<SOG.Polyline>(),
            boundary = polyline,
            units = _settingsStore.Current.SpeckleUnits
          };
        }
        // if it's already the last part, add to list
        if (idx == target.PartCount - 1)
        {
          converted.Add(polygonGeom);
        }
      }
      else
      {
        throw new ValidationException($"Patch type {patchType} is not supported");
      }
    }
    return converted;
  }
}
