using Speckle.Converters.Common;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.MicroStation.Services;

/// <summary>
/// Converts MicroStation 2026 <see cref="MeasurementUnit"/> to Speckle unit strings.
/// The COM <c>MeasurementUnit.Label</c> property contains an abbreviated string such as
/// "m", "mm", "ft", "in" that we map to Speckle's canonical unit constants.
/// </summary>
public sealed class MicroStationToSpeckleUnitConverter : IHostToSpeckleUnitConverter<MeasurementUnit>
{
  private static readonly Dictionary<string, string> s_labelMap = new(StringComparer.OrdinalIgnoreCase)
  {
    ["m"] = SSC.Units.Meters,
    ["mm"] = SSC.Units.Millimeters,
    ["cm"] = SSC.Units.Centimeters,
    ["km"] = SSC.Units.Kilometers,
    ["ft"] = SSC.Units.Feet,
    ["in"] = SSC.Units.Inches,
    ["yd"] = SSC.Units.Yards,
    ["mi"] = SSC.Units.Miles,
  };

  public string ConvertOrThrow(MeasurementUnit hostUnit)
  {
    if (!string.IsNullOrEmpty(hostUnit.Label) && s_labelMap.TryGetValue(hostUnit.Label, out var mapped))
    {
      return mapped;
    }

    throw new UnitNotSupportedException($"Unsupported MicroStation unit: Label=\"{hostUnit.Label}\"");
  }
}
