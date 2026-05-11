using Speckle.Converter.MicroStation.Settings;
using Speckle.Converters.Common;
using Speckle.Sdk.Models;
using MgdShape = Bentley.DgnPlatformNET.Elements.ShapeElement;

namespace Speckle.Converter.MicroStation.ToSpeckle.TopLevel;

/// <summary>
/// Converts a managed <see cref="MgdShape"/> (closed planar polygon — floor outlines, profile
/// regions, etc.) into a closed Speckle Polyline.
/// </summary>
public class ShapeElementConverter(IConverterSettingsStore<MicroStationConversionSettings> settingsStore)
{
  public Base Convert(MgdShape mgdShape)
  {
    var s = settingsStore.Current;
    var applicationId = ((ulong)mgdShape.ElementId).ToString();

    var cv =
      mgdShape.GetCurveVector()
      ?? throw new InvalidOperationException($"ShapeElement {applicationId} has no CurveVector.");
    return CurveVectorToSpeckleHelper.ToSpeckle(cv, s.SpeckleUnits, applicationId);
  }
}
