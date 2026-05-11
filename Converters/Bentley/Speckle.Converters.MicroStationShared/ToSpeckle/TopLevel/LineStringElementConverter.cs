using Speckle.Converter.MicroStation.Settings;
using Speckle.Converters.Common;
using Speckle.Sdk.Models;
using MgdLineString = Bentley.DgnPlatformNET.Elements.LineStringElement;

namespace Speckle.Converter.MicroStation.ToSpeckle.TopLevel;

/// <summary>
/// Converts a managed <see cref="MgdLineString"/> (open polyline) into a Speckle Polyline.
/// Note: the COM interop has no <c>LineStringElement</c> wrapper, so the previous COM-based
/// path was excluded — line-string elements were forced through <c>PointStringElement</c>.
/// The managed surface exposes them properly, so they get their own typed converter here.
/// </summary>
public class LineStringElementConverter(IConverterSettingsStore<MicroStationConversionSettings> settingsStore)
{
  public Base Convert(MgdLineString mgdLineString)
  {
    var s = settingsStore.Current;
    var applicationId = ((ulong)mgdLineString.ElementId).ToString();

    var cv =
      mgdLineString.GetCurveVector()
      ?? throw new InvalidOperationException($"LineStringElement {applicationId} has no CurveVector.");
    return CurveVectorToSpeckleHelper.ToSpeckle(cv, s.SpeckleUnits, applicationId);
  }
}
