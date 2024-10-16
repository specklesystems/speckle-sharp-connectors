using Speckle.Converters.Common;
using Speckle.Sdk.Common;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.Civil3dShared;

public class Civil3dToSpeckleUnitConverter : IHostToSpeckleUnitConverter<AAEC.BuiltInUnit>
{
  private static readonly IReadOnlyDictionary<AAEC.BuiltInUnit, string> s_unitsMapping = Create();

  private static IReadOnlyDictionary<AAEC.BuiltInUnit, string> Create()
  {
    var dict = new Dictionary<AAEC.BuiltInUnit, string>();

    // POC: we should have a unit test to confirm these are as expected and don't change
    dict[AAEC.BuiltInUnit.Kilometer] = Units.Kilometers;
    dict[AAEC.BuiltInUnit.Meter] = Units.Meters;
    dict[AAEC.BuiltInUnit.Centimeter] = Units.Centimeters;
    dict[AAEC.BuiltInUnit.Millimeter] = Units.Millimeters;
    dict[AAEC.BuiltInUnit.Mile] = Units.Miles;
    dict[AAEC.BuiltInUnit.Yards] = Units.Yards;
    dict[AAEC.BuiltInUnit.Foot] = Units.Feet;
    //dict[AAEC.BuiltInUnit.SurveyFoot] = Units.USFeet;
    dict[AAEC.BuiltInUnit.Inch] = Units.Inches;
    dict[AAEC.BuiltInUnit.Dimensionless] = Units.None;
    return dict;
  }

  public string ConvertOrThrow(AAEC.BuiltInUnit hostUnit)
  {
    if (s_unitsMapping.TryGetValue(hostUnit, out string value))
    {
      return value;
    }

    throw new UnitNotSupportedException($"The Unit System \"{hostUnit}\" is unsupported.");
  }
}
