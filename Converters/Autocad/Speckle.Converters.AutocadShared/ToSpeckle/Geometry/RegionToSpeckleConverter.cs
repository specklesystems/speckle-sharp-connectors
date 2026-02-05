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
  private readonly ITypedConverter<ADB.Curve, ICurve> _nurbConverter;
  private readonly ITypedConverter<ADB.Circle, SOG.Circle> _circleConverter;
  private readonly ITypedConverter<ADB.Ellipse, SOG.Ellipse> _ellipseConverter;
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;

  public RegionToSpeckleConverter(
    ITypedConverter<ABR.Brep, SOG.Mesh> brepConverter,
    ITypedConverter<AG.LineSegment3d, SOG.Line> lineConverter,
    ITypedConverter<AG.CircularArc3d, SOG.Arc> arcConverter,
    ITypedConverter<ADB.Curve, ICurve> nurbConverter,
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
    List<ICurve> innerLoops = ParseAndConvertBrepLoops(brepLoops, out ICurve? outerLoop);
    if (outerLoop is null)
    {
      throw new ConversionException("Could not convert outer region loop from brep.");
    }

    return new SOG.Region()
    {
      boundary = outerLoop,
      innerLoops = innerLoops,
      hasHatchPattern = false,
      displayValue = [mesh],
      units = _settingsStore.Current.SpeckleUnits,
    };
  }

  // Iterates through a list of brep boundary loops, converting them to Speckle and parsing between inner and outer loops
  private List<ICurve> ParseAndConvertBrepLoops(IEnumerable<ABR.BoundaryLoop> brepLoops, out ICurve? outerLoop)
  {
    List<ICurve> innerLoops = new();
    outerLoop = null;
    foreach (var loop in brepLoops)
    {
      List<AG.Curve3d> segments = new();
      foreach (ABR.Edge edge in loop.Edges)
      {
        if (edge.Curve is AG.ExternalCurve3d xCurve && xCurve.IsNativeCurve)
        {
          segments.Add(xCurve.NativeCurve);
        }
        else
        {
          throw new ConversionException("Unsupported curve type for Region conversion");
        }
      }

      ICurve convertedLoop =
        segments.Count == 1 ? ConvertSegmentToICurve(segments.First()) : ConvertSegmentsToICurve(segments);

      // sort inner or outer loop
      if (loop.LoopType == ABR.LoopType.LoopExterior)
      {
        outerLoop = convertedLoop;
      }
      else
      {
        innerLoops.Add(convertedLoop);
      }
    }

    return innerLoops;
  }

  private ICurve ConvertSegmentToICurve(AG.Curve3d segment)
  {
    switch (segment)
    {
      case AG.CircularArc3d arc: // expected to be closed
        return arc.StartPoint == arc.EndPoint
          ? _circleConverter.Convert(new ADB.Circle(arc.Center, arc.Normal, arc.Radius))
          : _arcConverter.Convert(arc);
      case AG.EllipticalArc3d ellipse:
        return _ellipseConverter.Convert(
          new ADB.Ellipse(
            ellipse.Center,
            ellipse.Normal,
            ellipse.MajorRadius * ellipse.MajorAxis,
            ellipse.MinorRadius / ellipse.MajorRadius,
            ellipse.StartAngle,
            ellipse.EndAngle
          )
        );
      case AG.NurbCurve3d nurbs:
        return _nurbConverter.Convert(ADB.Curve.CreateFromGeCurve(nurbs));
      default:
        throw new ConversionException($"Unsupported curve type for Region conversion: {segment}");
    }
  }

  private ICurve ConvertSegmentsToICurve(List<AG.Curve3d> segments)
  {
    return new SOG.Polycurve()
    {
      segments = segments.Select(x => ConvertSegment(x)).ToList(),
      closed = true,
      units = _settingsStore.Current.SpeckleUnits,
    };
  }

  private ICurve ConvertSegment(AG.Curve3d curve)
  {
    return curve switch
    {
      AG.LineSegment3d line => _lineConverter.Convert(line),
      AG.CircularArc3d arc => _arcConverter.Convert(arc),
      AG.NurbCurve3d nurb => _nurbConverter.Convert(ADB.Curve.CreateFromGeCurve(nurb)),
      _ => throw new ConversionException($"Unsupported curve type for Region conversion: {curve}"),
    };
  }
}
