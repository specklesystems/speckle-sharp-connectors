using Speckle.Converters.Common;
using Speckle.Converters.MicroStation.Settings;
using Speckle.Sdk.Models;
using MgdComplexString = Bentley.DgnPlatformNET.Elements.ComplexStringElement;

namespace Speckle.Converters.MicroStation.ToSpeckle.TopLevel;

/// <summary>
/// Converts a managed <see cref="MgdComplexString"/> (open compound chain of connected line /
/// arc / curve segments — e.g. an alignment baseline or pipe centerline) into an open Speckle
/// Polyline. The shared helper strokes any embedded arcs / b-splines.
/// </summary>
public class ComplexStringElementConverter(IConverterSettingsStore<MicroStationConversionSettings> settingsStore)
{
  public Base Convert(MgdComplexString mgdComplexString)
  {
    var s = settingsStore.Current;
    var applicationId = ((ulong)mgdComplexString.ElementId).ToString();

    var cv =
      mgdComplexString.GetCurveVector()
      ?? throw new InvalidOperationException($"ComplexStringElement {applicationId} has no CurveVector.");
    return CurveVectorToSpeckleHelper.ToSpeckle(cv, s.SpeckleUnits, applicationId);
  }
}
