using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Autocad.Geometry;

[NameAndRankValue(typeof(ADB.Region), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class RegionToSpeckleConverter : IToSpeckleTopLevelConverter, ITypedConverter<ADB.Region, SOG.Region>
{
  private readonly ITypedConverter<ABR.Brep, SOG.Mesh> _brepConverter;
  private readonly ITypedConverter<AG.LineSegment3d, SOG.Line> _lineConverter;
  private readonly ITypedConverter<AG.CircularArc3d, SOG.Arc> _arcConverter;
  private readonly ITypedConverter<AG.NurbCurve3d, SOG.Curve> _nurbConverter;
  private readonly ITypedConverter<ADB.Circle, SOG.Circle> _circleConverter;
  private readonly ITypedConverter<ADB.Ellipse, SOG.Ellipse> _ellipseConverter;
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;

  public RegionToSpeckleConverter(
    ITypedConverter<ABR.Brep, SOG.Mesh> brepConverter,
    ITypedConverter<AG.LineSegment3d, SOG.Line> lineConverter,
    ITypedConverter<AG.CircularArc3d, SOG.Arc> arcConverter,
    ITypedConverter<AG.NurbCurve3d, SOG.Curve> nurbConverter,
    ITypedConverter<ADB.Circle, SOG.Circle> circleConverter,
    ITypedConverter<ADB.Ellipse, SOG.Ellipse> ellipseConverter,
    IConverterSettingsStore<AutocadConversionSettings> settingsStore
  )
  {
    _brepConverter = brepConverter;
    _lineConverter = lineConverter;
    _arcConverter = arcConverter;
    _nurbConverter = nurbConverter;
    _circleConverter = circleConverter;
    _ellipseConverter = ellipseConverter;
    _settingsStore = settingsStore;
  }

  public Base Convert(object target) => Convert((ADB.Region)target);

  public SOG.Region Convert(ADB.Region target)
  {
    // generate Mesh for displayValue
    using ABR.Brep brep = new(target);
    if (brep.IsNull)
    {
      throw new ConversionException("Could not retrieve brep from the region.");
    }

    SOG.Mesh mesh = _brepConverter.Convert(brep);
    mesh.area = target.Area;

    // get all brep loops: can consist of LineSegment3d or CircularArc3d edges
    var brepLoops = brep
      .Complexes.SelectMany(complex => complex.Shells)
      .SelectMany(shell => shell.Faces)
      .SelectMany(face => face.Loops);

    // Get and convert boundary and inner loops
    var boundary = GetConvertedLoops(brepLoops, true)[0];
    var innerLoops = GetConvertedLoops(brepLoops, false);

    return new SOG.Region()
    {
      boundary = boundary,
      innerLoops = innerLoops,
      hasHatchPattern = false,
      displayValue = [mesh],
      units = _settingsStore.Current.SpeckleUnits
    };
  }

  private List<ICurve> GetConvertedLoops(IEnumerable<ABR.BoundaryLoop> brepLoops, bool getOuterLoop)
  {
    var loops = new List<ICurve>();
    foreach (var loop in brepLoops)
    {
      bool outer = loop.LoopType == ABR.LoopType.LoopExterior;

      // continue only if the loop type is as requester (outer or inner)
      if ((outer && getOuterLoop) || (!outer && !getOuterLoop))
      {
        // create segment collection for the current loop
        var segments = new List<AG.Curve3d>();
        foreach (var edge in loop.Edges)
        {
          var curve = edge.Curve;
          if (curve is AG.ExternalCurve3d xCurve && xCurve.IsNativeCurve)
          {
            segments.Add(xCurve.NativeCurve);
          }
          else
          {
            throw new ConversionException("Unsupported curve type for Region conversion");
          }
        }
        // reverse segment collection with arcs in case end-start points of subsequent segments don't match
        if (segments.Count > 1 && Math.Abs(segments[0].EndPoint.DistanceTo(segments[1].StartPoint)) > 0.00001)
        {
          segments.Reverse();
        }

        // convert segments to Speckle
        var convertedLoop = ConvertSegmentsToICurve(segments);
        loops.Add(convertedLoop);
      }
    }

    return loops;
  }

  private ICurve ConvertSegmentsToICurve(List<AG.Curve3d> segments)
  {
    ICurve convertedLoop;

    // Handle edge case: if the segment is a closed Arc, then use Circle conversion to create a valid shape.
    // Also, closed arcs cause errors when receiving in other host apps, like Rhino.
    if (segments.Count == 1 && segments[0] is AG.CircularArc3d arc && arc.StartAngle + arc.EndAngle == 0)
    {
      convertedLoop = _circleConverter.Convert(
        new ADB.Circle(arc.GetPlane().PointOnPlane, arc.GetPlane().Normal, arc.Radius)
      );
    }
    // Another edge case: closed Ellipse.
    else if (
      segments.Count == 1
      && segments[0] is AG.EllipticalArc3d ellipse
      && Math.Abs(ellipse.EndAngle - ellipse.StartAngle) - 2 * Math.PI < 0.0001
    )
    {
      convertedLoop = _ellipseConverter.Convert(
        new ADB.Ellipse(
          new(ellipse.Center.X, ellipse.Center.Y, 0),
          AG.Vector3d.ZAxis,
          new AG.Vector3d(ellipse.MajorAxis.X, ellipse.MajorAxis.Y, 0),
          ellipse.MinorRadius / ellipse.MajorRadius,
          ellipse.StartAngle,
          ellipse.EndAngle
        )
      );
    }
    // otherwise, just construct a Polycurve from subsequent segments
    else
    {
      // Maybe we need to convert to AutoCAD Polycurve
      convertedLoop = new SOG.Polycurve()
      {
        segments = segments.Select(x => ConvertSegment(x)).ToList(),
        closed = true,
        units = _settingsStore.Current.SpeckleUnits
      };
    }

    return convertedLoop;
  }

  private ICurve ConvertSegment(AG.Curve3d curve)
  {
    switch (curve)
    {
      case AG.LineSegment3d line:
        return _lineConverter.Convert(line);
      case AG.CircularArc3d arc:
        return _arcConverter.Convert(arc);
      case AG.NurbCurve3d nurb:
        return _nurbConverter.Convert(nurb);
    }

    throw new ConversionException($"Unsupported curve type for Region conversion: {curve}");
  }
}
