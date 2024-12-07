using Speckle.Converters.Common;
using Speckle.Sdk.Common;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.CSiShared;

/// <summary>
/// Convert CSi eUnits enumeration to Speckle units.
/// </summary>
/// <remarks>
/// CSi GetPresentUnits() valid for both SAP 2000 and ETABS.
/// Represents units transmitted through API calls and not necessarily those displayed in GUI.
/// </remarks>
public class CsiToSpeckleUnitConverter : IHostToSpeckleUnitConverter<eUnits>
{
  private readonly Dictionary<eUnits, string> _unitMapping = new Dictionary<eUnits, string>();

  public CsiToSpeckleUnitConverter()
  {
    _unitMapping[eUnits.lb_in_F] = Units.Inches;
    _unitMapping[eUnits.lb_ft_F] = Units.Feet;
    _unitMapping[eUnits.kip_in_F] = Units.Inches;
    _unitMapping[eUnits.kip_ft_F] = Units.Feet;
    _unitMapping[eUnits.kN_mm_C] = Units.Millimeters;
    _unitMapping[eUnits.kN_m_C] = Units.Meters;
    _unitMapping[eUnits.kgf_mm_C] = Units.Millimeters;
    _unitMapping[eUnits.kgf_m_C] = Units.Meters;
    _unitMapping[eUnits.N_mm_C] = Units.Millimeters;
    _unitMapping[eUnits.N_m_C] = Units.Meters;
    _unitMapping[eUnits.Ton_mm_C] = Units.Millimeters;
    _unitMapping[eUnits.Ton_m_C] = Units.Meters;
    _unitMapping[eUnits.kN_cm_C] = Units.Centimeters;
    _unitMapping[eUnits.kgf_cm_C] = Units.Centimeters;
    _unitMapping[eUnits.N_cm_C] = Units.Centimeters;
    _unitMapping[eUnits.Ton_cm_C] = Units.Centimeters;
  }

  public string ConvertOrThrow(eUnits hostUnit) =>
    _unitMapping.TryGetValue(hostUnit, out string? value)
      ? value
      : throw new UnitNotSupportedException($"The Unit System \"{hostUnit}\" is unsupported.");
}
