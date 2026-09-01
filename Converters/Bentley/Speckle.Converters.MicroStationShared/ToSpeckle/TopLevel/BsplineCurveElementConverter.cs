using Speckle.Converters.Common;
using Speckle.Converters.MicroStation.Settings;
using Speckle.Sdk.Models;
using MgdBSplineCurve = Bentley.DgnPlatformNET.Elements.BSplineCurveElement;

namespace Speckle.Converters.MicroStation.ToSpeckle.TopLevel;

/// <summary>
/// Converts a managed <see cref="MgdBSplineCurve"/> (NURBS curve) into a stroked Speckle
/// Polyline. The shared helper strokes the underlying b-spline at chord tolerance via the
/// curve's <c>MSBsplineCurve</c> proxy. Preserving the NURBS as Speckle <c>Curve</c>
/// (control poles + knots + weights) is a follow-up.
/// </summary>
public class BsplineCurveElementConverter(IConverterSettingsStore<MicroStationConversionSettings> settingsStore)
{
  public Base Convert(MgdBSplineCurve mgdBspline)
  {
    var s = settingsStore.Current;
    var applicationId = ((ulong)mgdBspline.ElementId).ToString();

    var cv =
      mgdBspline.GetCurveVector()
      ?? throw new InvalidOperationException($"BSplineCurveElement {applicationId} has no CurveVector.");
    return CurveVectorToSpeckleHelper.ToSpeckle(cv, s.SpeckleUnits, applicationId);
  }
}
