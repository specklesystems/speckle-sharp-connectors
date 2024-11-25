using Speckle.Converters.Common;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converter.Navisworks.Services;

public sealed class NavisworksToSpeckleUnitConverter : IHostToSpeckleUnitConverter<NAV.Units>
{
  private readonly Dictionary<NAV.Units, string> _unitMapping = [];

  public NavisworksToSpeckleUnitConverter()
  {
    _unitMapping[NAV.Units.Millimeters] = SSC.Units.Millimeters;
    _unitMapping[NAV.Units.Centimeters] = SSC.Units.Centimeters;
    _unitMapping[NAV.Units.Meters] = SSC.Units.Meters;
    _unitMapping[NAV.Units.Inches] = SSC.Units.Inches;
    _unitMapping[NAV.Units.Feet] = SSC.Units.Feet;
    _unitMapping[NAV.Units.Miles] = SSC.Units.Miles;
    _unitMapping[NAV.Units.Kilometers] = SSC.Units.Kilometers;
    _unitMapping[NAV.Units.Yards] = SSC.Units.Yards;
  }

  public string ConvertOrThrow(NAV.Units hostUnit) =>
    _unitMapping.TryGetValue(hostUnit, out string? value)
      ? value
      : throw new UnitNotSupportedException($"The Unit System \"{hostUnit}\" is unsupported.");
}
