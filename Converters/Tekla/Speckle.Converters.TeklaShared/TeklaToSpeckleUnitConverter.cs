using Speckle.Converters.Common;
using Speckle.Sdk.Common.Exceptions;
using Tekla.Structures.Datatype;
using SSC = Speckle.Sdk.Common;

namespace Speckle.Converter.Tekla2024;

public class TeklaToSpeckleUnitConverter : IHostToSpeckleUnitConverter<Distance.UnitType>
{
  private readonly Dictionary<Distance.UnitType, string> _unitMapping = new();

  public TeklaToSpeckleUnitConverter()
  {
    _unitMapping[Distance.UnitType.Millimeter] = SSC.Units.Millimeters;
    _unitMapping[Distance.UnitType.Centimeter] = SSC.Units.Centimeters;
    _unitMapping[Distance.UnitType.Meter] = SSC.Units.Meters;
    _unitMapping[Distance.UnitType.Inch] = SSC.Units.Inches;
    _unitMapping[Distance.UnitType.Foot] = SSC.Units.Feet;
  }

  public string ConvertOrThrow(Distance.UnitType hostUnit) =>
    _unitMapping.TryGetValue(hostUnit, out string? value)
      ? value
      : throw new UnitNotSupportedException($"The Unit System \"{hostUnit}\" is unsupported.");
}
