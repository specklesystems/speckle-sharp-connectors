using ArcGIS.Core.Geometry;
using Speckle.Converters.Common;
using Speckle.Sdk;
using Speckle.Sdk.Common;

namespace Speckle.Converters.ArcGIS3;

public class ArcGISToSpeckleUnitConverter : IHostToSpeckleUnitConverter<Unit>
{
  private static readonly IReadOnlyDictionary<int, string> s_unitMapping = Create();

  private static IReadOnlyDictionary<int, string> Create()
  {
    // POC: we should have a unit test to confirm these are as expected and don't change
    // more units: https://pro.arcgis.com/en/pro-app/latest/sdk/api-reference/topic8349.html
    var dict = new Dictionary<int, string>();
    dict[LinearUnit.Millimeters.FactoryCode] = Units.Millimeters;
    dict[LinearUnit.Centimeters.FactoryCode] = Units.Centimeters;
    dict[LinearUnit.Meters.FactoryCode] = Units.Meters;
    dict[LinearUnit.Kilometers.FactoryCode] = Units.Kilometers;
    dict[LinearUnit.Inches.FactoryCode] = Units.Inches;
    dict[LinearUnit.Feet.FactoryCode] = Units.Feet;
    dict[LinearUnit.Yards.FactoryCode] = Units.Yards;
    dict[LinearUnit.Miles.FactoryCode] = Units.Miles;
    dict[9003] = Units.USFeet;
    return dict;
  }

  public string ConvertOrThrow(Unit hostUnit)
  {
    // allow to send data in degrees (RootObjBuilder will send a warning)
    if (hostUnit.UnitType == UnitType.Angular && hostUnit.FactoryCode == 9102)
    {
      return Units.Meters;
    }

    int linearUnit = LinearUnit.CreateLinearUnit(hostUnit.Wkt).FactoryCode;

    if (s_unitMapping.TryGetValue(linearUnit, out string? value))
    {
      return value;
    }

    // POC: probably would prefer something more specific
    throw new SpeckleException($"The Unit System \"{hostUnit}\" is unsupported.");
  }
}
