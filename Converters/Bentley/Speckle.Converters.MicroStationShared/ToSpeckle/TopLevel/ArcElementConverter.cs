using Speckle.Converters.Common;
using Speckle.Converters.MicroStation.Settings;
using Speckle.Sdk.Models;
using MgdArc = Bentley.DgnPlatformNET.Elements.ArcElement;

namespace Speckle.Converters.MicroStation.ToSpeckle.TopLevel;

/// <summary>
/// Converts a managed <see cref="MgdArc"/> into a stroked Speckle Polyline. Bentley's managed
/// <c>ArcElement</c> doesn't expose center/radii directly — the geometry lives in its
/// <c>CurveVector</c> as an Arc primitive with a <c>DEllipse3d</c>. The shared helper strokes
/// the arc into a polyline at sub-millimetre tolerance.
/// <para>
/// Type fidelity (preserve Speckle <c>Arc</c> / <c>Circle</c> / <c>Ellipse</c> instead of stroking)
/// is a follow-up — would require switching on the DEllipse3d's axis ratio + sweep and building
/// the appropriate Plane / startPoint / midPoint / endPoint structure.
/// </para>
/// </summary>
public class ArcElementConverter(IConverterSettingsStore<MicroStationConversionSettings> settingsStore)
{
  public Base Convert(MgdArc mgdArc)
  {
    var s = settingsStore.Current;
    var applicationId = ((ulong)mgdArc.ElementId).ToString();

    var cv =
      mgdArc.GetCurveVector() ?? throw new InvalidOperationException($"ArcElement {applicationId} has no CurveVector.");
    return CurveVectorToSpeckleHelper.ToSpeckle(cv, s.SpeckleUnits, applicationId);
  }
}
