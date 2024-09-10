using Autodesk.Revit.DB;
using Speckle.Converters.Common;

namespace Speckle.Converters.RevitShared.Services;

public sealed class ScalingServiceToHost
{
  public double ScaleToNative(double value, string units)
  {
    if (string.IsNullOrEmpty(units))
    {
      return value;
    }

    return ScaleToNative(
      value,
      UnitsToNative(units) ?? throw new SpeckleConversionException($"The Unit System \"{units}\" is unsupported.")
    );
  }

  public double ScaleToNative(double value, ForgeTypeId typeId)
  {
    return UnitUtils.ConvertToInternalUnits(value, typeId);
  }

  public ForgeTypeId? UnitsToNative(string units)
  {
    var u = Sdk.Common.Units.GetUnitsFromString(units);

    return u switch
    {
      Sdk.Common.Units.Millimeters => UnitTypeId.Millimeters,
      Sdk.Common.Units.Centimeters => UnitTypeId.Centimeters,
      Sdk.Common.Units.Meters => UnitTypeId.Meters,
      Sdk.Common.Units.Inches => UnitTypeId.Inches,
      Sdk.Common.Units.Feet => UnitTypeId.Feet,
      _ => null,
    };
  }
}
