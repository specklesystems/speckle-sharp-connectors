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
    var u = Core.Kits.Units.GetUnitsFromString(units);

    return u switch
    {
      Core.Kits.Units.Millimeters => UnitTypeId.Millimeters,
      Core.Kits.Units.Centimeters => UnitTypeId.Centimeters,
      Core.Kits.Units.Meters => UnitTypeId.Meters,
      Core.Kits.Units.Inches => UnitTypeId.Inches,
      Core.Kits.Units.Feet => UnitTypeId.Feet,
      _ => null,
    };
  }
}
