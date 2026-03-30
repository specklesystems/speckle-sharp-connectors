using Autodesk.AutoCAD.DatabaseServices;
using Speckle.Converters.Common;
using Speckle.Sdk.Common;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.Plant3dShared;

public class Plant3dToSpeckleUnitConverter : IHostToSpeckleUnitConverter<UnitsValue>
{
  private static readonly IReadOnlyDictionary<UnitsValue, string> s_unitsMapping = Create();

  private static IReadOnlyDictionary<UnitsValue, string> Create()
  {
    var dict = new Dictionary<UnitsValue, string>();

    dict[UnitsValue.Undefined] = Units.Meters;
    dict[UnitsValue.Millimeters] = Units.Millimeters;
    dict[UnitsValue.Centimeters] = Units.Centimeters;
    dict[UnitsValue.Meters] = Units.Meters;
    dict[UnitsValue.Kilometers] = Units.Kilometers;
    dict[UnitsValue.Inches] = Units.Inches;
    dict[UnitsValue.Feet] = Units.Feet;
    dict[UnitsValue.Yards] = Units.Yards;
    dict[UnitsValue.Miles] = Units.Miles;
    return dict;
  }

  public string ConvertOrThrow(UnitsValue hostUnit)
  {
    if (s_unitsMapping.TryGetValue(hostUnit, out string? value))
    {
      return value;
    }

    throw new UnitNotSupportedException($"The Unit System \"{hostUnit}\" is unsupported.");
  }
}
