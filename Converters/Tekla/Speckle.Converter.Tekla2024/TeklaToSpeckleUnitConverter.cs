using Speckle.Converters.Common;
using Speckle.Sdk.Common.Exceptions;
using SSC = Speckle.Sdk.Common;
using TSD = Tekla.Structures.Drawing;
using System.Collections.Generic;

namespace Speckle.Converter.Tekla2024;

public class TeklaToSpeckleUnitConverter : IHostToSpeckleUnitConverter<TSD.Units>
{
  private readonly Dictionary<TSD.Units, string> _unitMapping = new();

  public TeklaToSpeckleUnitConverter()
  {
    _unitMapping[TSD.Units.Automatic] = SSC.Units.Millimeters;
    _unitMapping[TSD.Units.Millimeters] = SSC.Units.Millimeters;
    _unitMapping[TSD.Units.Centimeters] = SSC.Units.Centimeters;
    _unitMapping[TSD.Units.Meters] = SSC.Units.Meters;
    _unitMapping[TSD.Units.Inches] = SSC.Units.Inches;
    _unitMapping[TSD.Units.Feet] = SSC.Units.Feet;

    // there are also other units in tekla, not sure how to handle them in speckle
    // auto unit option in tekla is based on the selected environment
    //_unitMapping[TSD.Units.FeetAndInches]
    //_unitMapping[TSD.Units.CentimetersOrMeters]
  }

  public string ConvertOrThrow(TSD.Units hostUnit)
  {
    if (_unitMapping.TryGetValue(hostUnit, out string? value))
    {
      return value;
    }

    throw new UnitNotSupportedException($"The Unit System \"{hostUnit}\" is unsupported.");
  }
}
