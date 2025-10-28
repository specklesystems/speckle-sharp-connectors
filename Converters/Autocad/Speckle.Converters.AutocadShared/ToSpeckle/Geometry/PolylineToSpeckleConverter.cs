using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Autocad.Geometry;

/// <summary>
/// The <see cref="ADB.Polyline"/> class converter. Converts to <see cref="SOG.Autocad.AutocadPolycurve"/>.
/// </summary>
/// <remarks>
/// <see cref="ADB.Polyline"/> is of type <see cref="SOG.Autocad.AutocadPolyType.Light"/> and will have only <see cref="SOG.Line"/>s and <see cref="SOG.Arc"/>s in <see cref="SOG.Polycurve.segments"/>.
/// </remarks>
[NameAndRankValue(typeof(ADB.Polyline), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class PolylineToSpeckleConverter
  : IToSpeckleTopLevelConverter,
    ITypedConverter<ADB.Polyline, SOG.Autocad.AutocadPolycurve>
{
  private readonly ITypedConverter<AG.LineSegment3d, SOG.Line> _lineConverter;
  private readonly ITypedConverter<AG.CircularArc3d, SOG.Arc> _arcConverter;

  private readonly ITypedConverter<AG.Vector3d, SOG.Vector> _vectorConverter;
  private readonly IReferencePointConverter _referencePointConverter;
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;

  public PolylineToSpeckleConverter(
    ITypedConverter<AG.LineSegment3d, SOG.Line> lineConverter,
    ITypedConverter<AG.CircularArc3d, SOG.Arc> arcConverter,
    ITypedConverter<AG.Vector3d, SOG.Vector> vectorConverter,
    IReferencePointConverter referencePointConverter,
    IConverterSettingsStore<AutocadConversionSettings> settingsStore
  )
  {
    _lineConverter = lineConverter;
    _arcConverter = arcConverter;
    _vectorConverter = vectorConverter;
    _referencePointConverter = referencePointConverter;
    _settingsStore = settingsStore;
  }

  public Base Convert(object target) => Convert((ADB.Polyline)target);

  public SOG.Autocad.AutocadPolycurve Convert(ADB.Polyline target)
  {
    List<double> value = new(target.NumberOfVertices * 3);
    List<double> bulges = new(target.NumberOfVertices);
    List<Objects.ICurve> segments = new();
    for (int i = 0; i < target.NumberOfVertices; i++)
    {
      // get vertex value in the World Coordinate System (WCS)
      AG.Point3d vertex = target.GetPoint3dAt(i);
      value.Add(vertex.X);
      value.Add(vertex.Y);
      value.Add(vertex.Z);

      // get the bulge
      bulges.Add(target.GetBulgeAt(i));

      // get segment in the Global Coordinate System (GCS)
      ADB.SegmentType type = target.GetSegmentType(i);
      switch (type)
      {
        case ADB.SegmentType.Line:
          AG.LineSegment3d line = target.GetLineSegmentAt(i);
          segments.Add(_lineConverter.Convert(line));
          break;
        case ADB.SegmentType.Arc:
          AG.CircularArc3d arc = target.GetArcSegmentAt(i);
          segments.Add(_arcConverter.Convert(arc));
          break;
        default:
          // we are skipping segments of type Empty, Point, and Coincident
          break;
      }
    }

    SOG.Vector normal = _vectorConverter.Convert(target.Normal);

    // get the elevation transformed by ucs
    double elevation = target.Elevation;
    if (_settingsStore.Current.ReferencePointTransform is AG.Matrix3d ucsToWcs)
    {
      elevation = TransformElevationByUCS(target.Normal, elevation, ucsToWcs);
    }

    SOG.Autocad.AutocadPolycurve polycurve =
      new()
      {
        segments = segments,
        value = _referencePointConverter.ConvertDoublesToExternalCoordinates(value), // convert with reference point
        bulges = bulges,
        normal = normal,
        tangents = null,
        elevation = elevation,
        polyType = SOG.Autocad.AutocadPolyType.Light,
        closed = target.Closed,
        length = target.Length,
        area = target.Area,
        units = _settingsStore.Current.SpeckleUnits
      };

    return polycurve;
  }

  /// <summary>
  /// The elevation prop is actually the perpendicular distance of the OCS plane from WCS origin
  /// So to get the elevation in UCS, we need to get that perpendicular distance measured in UCS’s coordinate axes fml
  /// </summary>
  /// <param name="normal">in WCS</param>
  /// <param name="elevation"></param>
  /// <param name="ucsToWcs"></param>
  /// <returns></returns>
  private double TransformElevationByUCS(AG.Vector3d normal, double elevation, AG.Matrix3d ucsToWcs)
  {
    // get a point in wcs on the plane
    AG.Point3d wcsPoint = AG.Point3d.Origin + normal * elevation;

    // Transform into UCS
    AG.Matrix3d wcsToUcs = ucsToWcs.Inverse();
    AG.Point3d ucsPoint = wcsPoint.TransformBy(wcsToUcs);
    AG.Vector3d ucsNormal = normal.TransformBy(wcsToUcs);

    // Compute UCS elevation as plane offset
    double ucsElevation = ucsPoint.GetAsVector().DotProduct(ucsNormal);
    return ucsElevation;
  }
}
