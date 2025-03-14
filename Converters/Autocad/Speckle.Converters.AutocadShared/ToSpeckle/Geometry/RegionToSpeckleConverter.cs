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
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;

  public RegionToSpeckleConverter(
    ITypedConverter<ABR.Brep, SOG.Mesh> brepConverter,
    ITypedConverter<AG.LineSegment3d, SOG.Line> lineConverter,
    ITypedConverter<AG.CircularArc3d, SOG.Arc> arcConverter,
    IConverterSettingsStore<AutocadConversionSettings> settingsStore
  )
  {
    _brepConverter = brepConverter;
    _lineConverter = lineConverter;
    _arcConverter = arcConverter;
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

    // get boundary from brep: can by LineSegment3d or CircularArc3d
    var boundarySegments = GetLoops(brep, true)[0];
    var boundary = new SOG.Polycurve()
    {
      segments = boundarySegments.Select(x => ConvertRegionLoopSegment(x)).ToList(),
      closed = true,
      units = _settingsStore.Current.SpeckleUnits
    };

    // get inner loops from brep: can by LineSegment3d or CircularArc3d
    var loopsAllSegments = GetLoops(brep, false);
    var innerLoops = new List<ICurve>();
    foreach (var loopSegments in loopsAllSegments)
    {
      var loop = new SOG.Polycurve()
      {
        segments = loopSegments.Select(x => ConvertRegionLoopSegment(x)).ToList(),
        closed = true,
        units = _settingsStore.Current.SpeckleUnits
      };
      innerLoops.Add(loop);
    }

    return new SOG.Region()
    {
      boundary = boundary,
      innerLoops = innerLoops,
      hasHatchPattern = false,
      displayValue = [mesh],
      units = _settingsStore.Current.SpeckleUnits
    };
  }

  private ICurve ConvertRegionLoopSegment(AG.Curve3d curve)
  {
    switch (curve)
    {
      case AG.LineSegment3d line:
        return _lineConverter.Convert(line);
      case AG.CircularArc3d arc:
        return _arcConverter.Convert(arc);
    }

    throw new ConversionException("Unsupported curve type for Region conversion");
  }

  private List<List<AG.Curve3d>> GetLoops(ABR.Brep brep, bool getBoundary)
  {
    var loops = new List<List<AG.Curve3d>>();
    foreach (
      var loop in brep
        .Complexes.SelectMany(complex => complex.Shells)
        .SelectMany(shell => shell.Faces)
        .SelectMany(face => face.Loops)
    )
    {
      bool outer = loop.LoopType == ABR.LoopType.LoopExterior;

      if ((outer && getBoundary) || (!outer && !getBoundary))
      {
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

        segments.Reverse();
        loops.Add(segments);
      }
    }

    return loops;
  }
}
