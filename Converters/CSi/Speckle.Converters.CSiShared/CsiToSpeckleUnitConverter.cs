using Speckle.Converters.Common;
using Speckle.Sdk.Common;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.CSiShared;

/// <summary>
/// Convert CSi eLength enumeration to Speckle units.
/// </summary>
/// <remarks>
/// CSi GetPresentUnits_2() valid for ONLY ETABS. If we add SAP2000, this needs to be modified
/// Represents units transmitted through API calls and not necessarily those displayed in GUI.
/// </remarks>
public class CsiToSpeckleUnitConverter : IHostToSpeckleUnitConverter<eLength>
{
  private readonly Dictionary<eLength, string> _unitMapping = [];

  public CsiToSpeckleUnitConverter()
  {
    _unitMapping[eLength.NotApplicable] = Units.None;
    _unitMapping[eLength.inch] = Units.Inches;
    _unitMapping[eLength.ft] = Units.Feet;
    // _unitMapping[eLength.micron] = Units.None;
    _unitMapping[eLength.mm] = Units.Millimeters;
    _unitMapping[eLength.cm] = Units.Centimeters;
    _unitMapping[eLength.m] = Units.Meters;
  }

  public string ConvertOrThrow(eLength hostUnit) =>
    _unitMapping.TryGetValue(hostUnit, out string? value)
      ? value
      : throw new UnitNotSupportedException($"The Unit System \"{hostUnit}\" is unsupported.");
}
