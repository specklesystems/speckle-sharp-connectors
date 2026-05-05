using Speckle.Converter.MicroStation.Settings;
using Speckle.Converters.Common;
using Speckle.Objects.Geometry;
using Speckle.Sdk.Models;

namespace Speckle.Converter.MicroStation.ToSpeckle.TopLevel;

/// <summary>
/// Converts a MicroStation 2026 COM <see cref="MSIDGN.PointStringElement"/> (open polyline)
/// to a Speckle <see cref="Polyline"/>.
/// <see cref="MSIDGN.PointStringElement.GetVertices"/> returns coordinates already in master units.
/// </summary>
[NameAndRankValue(typeof(MSIDGN.PointStringElement), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class PointStringElementConverter(IConverterSettingsStore<MicroStationConversionSettings> settingsStore)
  : IToSpeckleTopLevelConverter
{
  public Base Convert(object target) => Convert((MSIDGN.PointStringElement)target);

  private Polyline Convert(MSIDGN.PointStringElement element)
  {
    var s = settingsStore.Current;
    var pts = element.GetVertices();
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
      units = s.SpeckleUnits,
      applicationId = element.ID.ToString(),
    };
  }
}
