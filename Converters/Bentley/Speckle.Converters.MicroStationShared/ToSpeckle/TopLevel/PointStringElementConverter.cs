using Speckle.Converter.MicroStation.Settings;
using Speckle.Converters.Common;
using Speckle.Sdk.Models;
using MgdPointString = Bentley.DgnPlatformNET.Elements.PointStringElement;

namespace Speckle.Converter.MicroStation.ToSpeckle.TopLevel;

/// <summary>
/// Converts a managed <see cref="MgdPointString"/> (sequence of disconnected points used for
/// markers, control points, surveys, etc.) into a Speckle Polyline. We treat the points as
/// connected for visual representation — the underlying point set is preserved as the polyline
/// vertices, and consumers can split if they need disconnected semantics.
/// </summary>
public class PointStringElementConverter(IConverterSettingsStore<MicroStationConversionSettings> settingsStore)
{
  public Base Convert(MgdPointString mgdPointString)
  {
    var s = settingsStore.Current;
    var applicationId = ((ulong)mgdPointString.ElementId).ToString();

    var cv =
      mgdPointString.GetCurveVector()
      ?? throw new InvalidOperationException($"PointStringElement {applicationId} has no CurveVector.");
    return CurveVectorToSpeckleHelper.ToSpeckle(cv, s.SpeckleUnits, applicationId);
  }
}
