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
    AG.Vector3d normal = AG
      .Vector3d.ZAxis.TransformBy(_settingsStore.Current.Document.Editor.CurrentUserCoordinateSystem)
      .GetNormal();
    AG.Plane plane = new(AG.Point3d.Origin, normal);

    // An ADB.Polyline is planar: its vertices are 2D in the plane defined by Normal + Elevation. Convert2d projects the
    // scaled vertices onto the plane THROUGH THE ORIGIN, so without an explicit Elevation every planar polycurve bakes
    // at Z=0 — a lwpolyline sent from a drawing at elevation came back flattened [ENG-8819]. The 4.0 artefact path
    // always lands here (SGEO carries no AutocadPolycurve subtype, so the elevation-aware
    // AutocadPolycurveToHostPolylineRawConverter is unreachable), so recover it from the first vertex: the caller only
    // routes PLANAR polycurves here, so every vertex shares this offset along the normal.
    double? elevation = null;

    // Scales the point, records the plane offset from the first vertex, and projects it into the polyline's plane.
    AG.Point2d ToPlane(SOG.Point point)
    {
      AG.Point3d scaled = _pointConverter.Convert(point);
      elevation ??= scaled.GetAsVector().DotProduct(normal); // plane passes through the origin → offset is the dot
      return scaled.Convert2d(plane);
    }

    int count = 0;
    foreach (Objects.ICurve segment in target.segments)
    {
      switch (segment)
      {
        case SOG.Line o:
          polyline.AddVertexAt(count, ToPlane(o.start), 0, 0, 0);
          if (!target.closed && count == target.segments.Count - 1)
          {
            polyline.AddVertexAt(count + 1, ToPlane(o.end), 0, 0, 0);
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
          polyline.AddVertexAt(count, ToPlane(arc.startPoint), bulge, 0, 0);
          if (!target.closed && count == target.segments.Count - 1)
          {
            polyline.AddVertexAt(count + 1, ToPlane(arc.endPoint), 0, 0, 0);
          }

          count++;
          break;
        case SOG.Spiral o:
          foreach (SOG.Point vertex in o.displayValue.GetPoints())
          {
            polyline.AddVertexAt(count, ToPlane(vertex), 0, 0, 0);
            count++;
          }

          break;
        default:
          break;
      }
    }

    // Normal before Elevation: the elevation is measured along the normal.
    if (elevation is double e)
    {
      polyline.Normal = normal;
      polyline.Elevation = e;
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
