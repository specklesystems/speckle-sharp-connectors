using Speckle.Converters.Common;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.MicroStation.Services;

/// <summary>
/// Converts a managed <see cref="DPN.UnitDefinition"/> (the active model's master unit from
/// <c>ModelInfo.GetMasterUnit()</c>) to a Speckle unit string. Standard units map directly;
/// non-standard metric/english units fall back to the closest match via the metre factor
/// (numerator/denominator is units-per-metre for <see cref="DPN.UnitBase.Meter"/>-based units).
/// </summary>
public sealed class MicroStationToSpeckleUnitConverter : IHostToSpeckleUnitConverter<DPN.UnitDefinition>
{
  public string ConvertOrThrow(DPN.UnitDefinition hostUnit)
  {
    switch (hostUnit.IsStandardUnit)
    {
      case DPN.StandardUnit.MetricMeters:
        return SSC.Units.Meters;
      case DPN.StandardUnit.MetricMillimeters:
        return SSC.Units.Millimeters;
      case DPN.StandardUnit.MetricCentimeters:
        return SSC.Units.Centimeters;
      case DPN.StandardUnit.MetricKilometers:
        return SSC.Units.Kilometers;
      case DPN.StandardUnit.EnglishFeet:
      case DPN.StandardUnit.EnglishSurveyFeet:
        return SSC.Units.Feet;
      case DPN.StandardUnit.EnglishInches:
        return SSC.Units.Inches;
      case DPN.StandardUnit.EnglishYards:
        return SSC.Units.Yards;
      case DPN.StandardUnit.EnglishMiles:
      case DPN.StandardUnit.EnglishSurveyMiles:
        return SSC.Units.Miles;
      default:
        break;
    }

    // Non-standard linear unit: units-per-metre = Numerator/Denominator (UnitBase.Meter).
    // Snap to the nearest canonical Speckle unit (dgnextract's mapUnits does the same by label).
    if (hostUnit.Base == DPN.UnitBase.Meter && hostUnit.Denominator != 0)
    {
      double perMeter = hostUnit.Numerator / hostUnit.Denominator;
      (double factor, string unit)[] candidates =
      [
        (1.0, SSC.Units.Meters),
        (1000.0, SSC.Units.Millimeters),
        (100.0, SSC.Units.Centimeters),
        (0.001, SSC.Units.Kilometers),
        (1.0 / 0.3048, SSC.Units.Feet),
        (1.0 / 0.0254, SSC.Units.Inches),
        (1.0 / 0.9144, SSC.Units.Yards),
        (1.0 / 1609.344, SSC.Units.Miles),
      ];
      foreach (var (factor, unit) in candidates)
      {
        if (Math.Abs(perMeter - factor) / factor < 1e-6)
        {
          return unit;
        }
      }
    }

    throw new UnitNotSupportedException(
      $"Unsupported MicroStation unit: \"{hostUnit.Label}\" ({hostUnit.IsStandardUnit})"
    );
  }
}
