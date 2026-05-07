using Speckle.Converter.MicroStation.Settings;
using Speckle.Converters.Common;
using Speckle.Objects.Geometry;
using Speckle.Sdk.Models;

namespace Speckle.Converter.MicroStation.ToSpeckle.TopLevel;

/// <summary>
/// Converts a MicroStation 2026 COM <see cref="MSIDGN.ComplexStringElement"/> (compound OPEN
/// chain of connected line/arc/curve segments — e.g. an alignment baseline or rail profile)
/// to an open Speckle <see cref="Polyline"/>.
/// <see cref="MSIDGN.ComplexStringElement.ConstructVertexList(double)"/> strokes any embedded
/// arcs/curves at the supplied chord tolerance.
/// </summary>
[NameAndRankValue(typeof(MSIDGN.ComplexStringElement), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class ComplexStringElementConverter(IConverterSettingsStore<MicroStationConversionSettings> settingsStore)
  : IToSpeckleTopLevelConverter
{
  private const double STROKE_TOLERANCE = 0.001;

  public Base Convert(object target) => Convert((MSIDGN.ComplexStringElement)target);

  private Polyline Convert(MSIDGN.ComplexStringElement element)
  {
    var s = settingsStore.Current;
    var pts = element.ConstructVertexList(STROKE_TOLERANCE);
    var value = new List<double>(pts.Length * 3);

    foreach (var pt in pts)
    {
      value.Add(pt.X);
      value.Add(pt.Y);
      value.Add(pt.Z);
    }

    return new Polyline
    {
      value = value,
      closed = false,
      units = s.SpeckleUnits,
      applicationId = element.ID.ToString(),
    };
  }
}
