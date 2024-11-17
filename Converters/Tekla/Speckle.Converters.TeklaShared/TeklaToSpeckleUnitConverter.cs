using Speckle.Converters.Common;
using Speckle.Sdk.Common.Exceptions;
using SSC = Speckle.Sdk.Common;
using TSDT = Tekla.Structures.Datatype;

namespace Speckle.Converter.Tekla2024;

public class TeklaToSpeckleUnitConverter : IHostToSpeckleUnitConverter<TSDT.Distance.UnitType>
{
  private readonly Dictionary<TSDT.Distance.UnitType, string> _unitMapping = new();

  public TeklaToSpeckleUnitConverter() // NOTE: This was changed to use Datatype and not Drawing. To discuss.
  {
    _unitMapping[TSDT.Distance.UnitType.Millimeter] = SSC.Units.Millimeters;
    _unitMapping[TSDT.Distance.UnitType.Centimeter] = SSC.Units.Centimeters;
    _unitMapping[TSDT.Distance.UnitType.Meter] = SSC.Units.Meters;
    _unitMapping[TSDT.Distance.UnitType.Inch] = SSC.Units.Inches;
    _unitMapping[TSDT.Distance.UnitType.Foot] = SSC.Units.Feet;

    // There are other units in tekla, not sure how to handle them in speckle
    // auto unit option in tekla is based on the selected environment
    //_unitMapping[TSD.Units.FeetAndInches]
    //_unitMapping[TSD.Units.CentimetersOrMeters]
  }

  // NOTE: This works and reflects what is defined in the Setting > Options > Units and decimals ... but API always returns internal units?
  public string ConvertOrThrow(TSDT.Distance.UnitType hostUnit) =>
    _unitMapping.TryGetValue(hostUnit, out string? value)
      ? value
      : throw new UnitNotSupportedException($"The Unit System \"{hostUnit}\" is unsupported.");
}
