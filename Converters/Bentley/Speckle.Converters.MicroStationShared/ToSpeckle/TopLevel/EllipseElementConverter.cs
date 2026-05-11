using Speckle.Converter.MicroStation.Settings;
using Speckle.Converters.Common;
using Speckle.Sdk.Models;
using MgdEllipse = Bentley.DgnPlatformNET.Elements.EllipseElement;

namespace Speckle.Converter.MicroStation.ToSpeckle.TopLevel;

/// <summary>
/// Converts a managed <see cref="MgdEllipse"/> into a stroked, closed Speckle Polyline. Same
/// rationale as <c>ArcElementConverter</c> — managed <c>EllipseElement</c> exposes geometry
/// only via <c>CurveVector</c>, so we delegate to the shared stroking helper.
/// Type-preserving conversion (Speckle <c>Ellipse</c> / <c>Circle</c>) is a follow-up.
/// </summary>
public class EllipseElementConverter(IConverterSettingsStore<MicroStationConversionSettings> settingsStore)
{
  public Base Convert(MgdEllipse mgdEllipse)
  {
    var s = settingsStore.Current;
    var applicationId = ((ulong)mgdEllipse.ElementId).ToString();

    var cv =
      mgdEllipse.GetCurveVector()
      ?? throw new InvalidOperationException($"EllipseElement {applicationId} has no CurveVector.");
    return CurveVectorToSpeckleHelper.ToSpeckle(cv, s.SpeckleUnits, applicationId);
  }
}
