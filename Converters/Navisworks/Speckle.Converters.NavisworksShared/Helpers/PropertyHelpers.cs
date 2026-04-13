using System.Globalization;
using System.Text.RegularExpressions;
using Speckle.Objects.Geometry;

namespace Speckle.Converter.Navisworks.Helpers;

public static class PropertyHelpers
{
  private static readonly HashSet<string> s_excludedCategories = ["Geometry", "Metadata"];

  /// <summary>
  /// Adds a property to an object (either a Base object or a Dictionary) if the value is not null or empty.
  /// </summary>
  private static readonly Dictionary<NAV.VariantDataType, Func<NAV.VariantData, string, dynamic?>> s_typeHandlers =
    new()
    {
      { NAV.VariantDataType.Boolean, (value, _) => value.ToBoolean() },
      { NAV.VariantDataType.DisplayString, (value, _) => value.ToDisplayString() },
      { NAV.VariantDataType.IdentifierString, (value, _) => value.ToIdentifierString() },
      { NAV.VariantDataType.Int32, (value, _) => value.ToInt32() },
      { NAV.VariantDataType.Double, (value, _) => value.ToDouble() },
      { NAV.VariantDataType.DoubleAngle, (value, _) => value.ToDoubleAngle() },
      { NAV.VariantDataType.DoubleArea, (value, _) => value.ToDoubleArea() },
      { NAV.VariantDataType.DoubleLength, (value, _) => value.ToDoubleLength() },
      { NAV.VariantDataType.DoubleVolume, (value, _) => value.ToDoubleVolume() },
      { NAV.VariantDataType.DateTime, (value, _) => value.ToDateTime().ToString(CultureInfo.InvariantCulture) },
      { NAV.VariantDataType.NamedConstant, (value, _) => value.ToNamedConstant().DisplayName },
      { NAV.VariantDataType.None, (_, _) => null },
      { NAV.VariantDataType.Point2D, (_, _) => null },
      {
        NAV.VariantDataType.Point3D,
        (value, units) =>
        {
          var point = value.ToPoint3D();
          var pointProperty = new Point(point.X, point.Y, point.Z, units);
          return pointProperty.ToString();
        }
      },
    };

  internal static dynamic? ConvertPropertyValue(NAV.VariantData? value, string units)
  {
    if (value == null)
    {
      return null;
    }

    if (s_typeHandlers.TryGetValue(value.DataType, out var handler))
    {
      return handler(value, units);
    }

    return value.DataType is NAV.VariantDataType.None or NAV.VariantDataType.Point2D ? null : value.ToString();
  }

  internal static void AddPropertyIfNotNullOrEmpty(object baseObject, string propertyName, object? value)
  {
    switch (value)
    {
      case null:
        break;
      case string stringValue:
      {
        if (!string.IsNullOrEmpty(stringValue))
        {
          AssignProperty(baseObject, propertyName, value);
        }

        break;
      }
      default:
        AssignProperty(baseObject, propertyName, value);
        break;
    }
  }

  private static void AssignProperty(object baseObject, string propertyName, object value)
  {
    switch (baseObject)
    {
      case SSM.Base baseObj:
        baseObj[propertyName] = value;
        break;
      case Dictionary<string, object> baseDict:
        baseDict[propertyName] = value;
        break;
      default:
        throw new ArgumentException("Unsupported object type", nameof(baseObject));
    }
  }

  internal static string SanitizePropertyName(string name) =>
    name == "Item" ? "Item" : Regex.Replace(name, @"[\.\/\s]", "_");

  internal static bool ShouldSkipCategory(NAV.PropertyCategory propertyCategory) =>
    s_excludedCategories.Contains(propertyCategory.DisplayName);
}

internal static class UnitLabels
{
  internal static string Linear(NAV.Units u) =>
    u switch
    {
      NAV.Units.Kilometers => "Kilometers",
      NAV.Units.Meters => "Metres",
      NAV.Units.Centimeters => "Centimeters",
      NAV.Units.Millimeters => "Millimeters",
      NAV.Units.Micrometers => "Micrometers",
      NAV.Units.Miles => "Miles",
      NAV.Units.Yards => "Yards",
      NAV.Units.Feet => "Feet",
      NAV.Units.Inches => "Inches",
      NAV.Units.Mils => "Mils",
      NAV.Units.Microinches => "Microinches",
      _ => "Metres",
    };

  internal static string Area(NAV.Units u) => $"Square {Linear(u).ToLower()}";

  public static string Volume(NAV.Units u) => $"Cubic {Linear(u).ToLower()}";
}
