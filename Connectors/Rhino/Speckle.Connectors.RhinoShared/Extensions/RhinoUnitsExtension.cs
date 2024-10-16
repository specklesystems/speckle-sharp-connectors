using Rhino;
using Speckle.Sdk.Common;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Connectors.Rhino.Extensions;

public static class RhinoUnitsExtension
{
  public static string ToSpeckleString(this UnitSystem unitSystem)
  {
    switch (unitSystem)
    {
      case UnitSystem.None:
        return Units.Meters;
      case UnitSystem.Millimeters:
        return Units.Millimeters;
      case UnitSystem.Centimeters:
        return Units.Centimeters;
      case UnitSystem.Meters:
        return Units.Meters;
      case UnitSystem.Kilometers:
        return Units.Kilometers;
      case UnitSystem.Inches:
        return Units.Inches;
      case UnitSystem.Feet:
        return Units.Feet;
      case UnitSystem.Yards:
        return Units.Yards;
      case UnitSystem.Miles:
        return Units.Miles;
      case UnitSystem.Unset:
        return Units.Meters;
      default:
        throw new UnitNotSupportedException($"The Unit System \"{unitSystem}\" is unsupported.");
    }
  }
}
