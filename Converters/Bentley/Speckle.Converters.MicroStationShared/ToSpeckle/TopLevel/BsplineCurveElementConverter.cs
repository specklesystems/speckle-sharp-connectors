using Speckle.Converter.MicroStation.Settings;
using Speckle.Converters.Common;
using Speckle.Objects.Geometry;
using Speckle.Sdk.Models;

namespace Speckle.Converter.MicroStation.ToSpeckle.TopLevel;

/// <summary>
/// Converts a MicroStation 2026 COM <see cref="MSIDGN.BsplineCurveElement"/> (NURBS curve) to a
/// Speckle <see cref="Polyline"/> using the B-spline control pole polygon as an approximation.
/// </summary>
[NameAndRankValue(typeof(MSIDGN.BsplineCurveElement), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class BsplineCurveElementConverter(IConverterSettingsStore<MicroStationConversionSettings> settingsStore)
  : IToSpeckleTopLevelConverter
{
  public Base Convert(object target) => Convert((MSIDGN.BsplineCurveElement)target);

  private Polyline Convert(MSIDGN.BsplineCurveElement element)
  {
    var s = settingsStore.Current;
    var curve = element.ExtractBsplineCurve();
    var poles = curve.GetPoles(); // control polygon — approximation of the NURBS curve

    var value = new List<double>(poles.Length * 3);
    foreach (var pt in poles)
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
