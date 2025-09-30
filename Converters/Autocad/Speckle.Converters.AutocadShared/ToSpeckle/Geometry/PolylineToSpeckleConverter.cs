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
  private readonly ITypedConverter<ADB.Extents3d, SOG.Box> _boxConverter;
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;

  public PolylineToSpeckleConverter(
    ITypedConverter<AG.LineSegment3d, SOG.Line> lineConverter,
    ITypedConverter<AG.CircularArc3d, SOG.Arc> arcConverter,
    ITypedConverter<AG.Vector3d, SOG.Vector> vectorConverter,
    ITypedConverter<ADB.Extents3d, SOG.Box> boxConverter,
    IConverterSettingsStore<AutocadConversionSettings> settingsStore
  )
  {
    _lineConverter = lineConverter;
    _arcConverter = arcConverter;
    _vectorConverter = vectorConverter;
    _boxConverter = boxConverter;
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
      // get vertex value in the Object Coordinate System (OCS)
      AG.Point3d vertex = target.GetPoint3dAt(i);
      value.AddRange(vertex.ToArray());

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
    SOG.Box bbox = _boxConverter.Convert(target.GeometricExtents);

    SOG.Autocad.AutocadPolycurve polycurve =
      new()
      {
        segments = segments,
        value = value, // do not need to convert with reference point since OCS is used internally
        bulges = bulges,
        normal = normal,
        tangents = null,
        elevation = target.Elevation,
        polyType = SOG.Autocad.AutocadPolyType.Light,
        closed = target.Closed,
        length = target.Length,
        area = target.Area,
        bbox = bbox,
        units = _settingsStore.Current.SpeckleUnits
      };

    return polycurve;
  }
}
