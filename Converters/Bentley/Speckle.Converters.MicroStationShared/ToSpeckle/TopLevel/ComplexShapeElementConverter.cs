using Speckle.Converter.MicroStation.Settings;
using Speckle.Converters.Common;
using Speckle.Sdk.Models;
using MgdComplexShape = Bentley.DgnPlatformNET.Elements.ComplexShapeElement;

namespace Speckle.Converter.MicroStation.ToSpeckle.TopLevel;

/// <summary>
/// Converts a managed <see cref="MgdComplexShape"/> (closed compound made of multiple connected
/// line / arc / curve segments — e.g. structural member profiles with filleted corners) into a
/// closed Speckle Polyline. The shared helper strokes any embedded arcs / b-splines.
/// </summary>
public class ComplexShapeElementConverter(IConverterSettingsStore<MicroStationConversionSettings> settingsStore)
{
  public Base Convert(MgdComplexShape mgdComplexShape)
  {
    var s = settingsStore.Current;
    var applicationId = ((ulong)mgdComplexShape.ElementId).ToString();

    var cv =
      mgdComplexShape.GetCurveVector()
      ?? throw new InvalidOperationException($"ComplexShapeElement {applicationId} has no CurveVector.");
    return CurveVectorToSpeckleHelper.ToSpeckle(cv, s.SpeckleUnits, applicationId);
  }
}
