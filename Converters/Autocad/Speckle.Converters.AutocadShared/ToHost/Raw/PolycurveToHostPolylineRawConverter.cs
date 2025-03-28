using Speckle.Converters.Autocad;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.AutocadShared.ToHost.Raw;

/// <summary>
/// If polycurve segments consist of only with Line and Arc, we convert it as ADB.Polyline.
/// </summary>
public class PolycurveToHostPolylineRawConverter : ITypedConverter<SOG.Polycurve, ADB.Polyline>
{
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;
  private readonly ITypedConverter<SOG.Point, AG.Point3d> _pointConverter;

  public PolycurveToHostPolylineRawConverter(
    IConverterSettingsStore<AutocadConversionSettings> settingsStore,
    ITypedConverter<SOG.Point, AG.Point3d> pointConverter
  )
  {
    _settingsStore = settingsStore;
    _pointConverter = pointConverter;
  }

  public ADB.Polyline Convert(SOG.Polycurve target)
  {
    ADB.Polyline polyline = new() { Closed = target.closed };
    AG.Plane plane =
      new(
        AG.Point3d.Origin,
        AG.Vector3d.ZAxis.TransformBy(_settingsStore.Current.Document.Editor.CurrentUserCoordinateSystem)
      );

    int count = 0;
    foreach (Objects.ICurve segment in target.segments)
    {
      switch (segment)
      {
        case SOG.Line o:
          polyline.AddVertexAt(count, _pointConverter.Convert(o.start).Convert2d(plane), 0, 0, 0);
          if (!target.closed && count == target.segments.Count - 1)
          {
            polyline.AddVertexAt(count + 1, _pointConverter.Convert(o.end).Convert2d(plane), 0, 0, 0);
          }

          count++;
          break;
        case SOG.Arc arc:
          // POC: possibly endAngle and startAngle null?
          double measure = arc.measure;
          if (measure <= 0 || measure >= 2 * Math.PI)
          {
            throw new ArgumentOutOfRangeException(nameof(target), "Cannot convert arc with measure <= 0 or >= 2 pi");
          }

          var bulge = Math.Tan(measure / 4) * BulgeDirection(arc.startPoint, arc.midPoint, arc.endPoint);
          polyline.AddVertexAt(count, _pointConverter.Convert(arc.startPoint).Convert2d(plane), bulge, 0, 0);
          if (!target.closed && count == target.segments.Count - 1)
          {
            polyline.AddVertexAt(count + 1, _pointConverter.Convert(arc.endPoint).Convert2d(plane), 0, 0, 0);
          }

          count++;
          break;
        case SOG.Spiral o:
          List<AG.Point3d> vertices = o.displayValue.GetPoints().Select(_pointConverter.Convert).ToList();
          foreach (AG.Point3d vertex in vertices)
          {
            polyline.AddVertexAt(count, vertex.Convert2d(plane), 0, 0, 0);
            count++;
          }

          break;
        default:
          break;
      }
    }

    return polyline;
  }

  // calculates bulge direction: (-) clockwise, (+) counterclockwise
  private int BulgeDirection(SOG.Point start, SOG.Point mid, SOG.Point end)
  {
    // get vectors from points
    double[] v1 = new double[] { end.x - start.x, end.y - start.y, end.z - start.z }; // vector from start to end point
    double[] v2 = new double[] { mid.x - start.x, mid.y - start.y, mid.z - start.z }; // vector from start to mid point

    // calculate cross product z direction
    double z = v1[0] * v2[1] - v2[0] * v1[1];

    return z > 0 ? -1 : 1;
  }
}
