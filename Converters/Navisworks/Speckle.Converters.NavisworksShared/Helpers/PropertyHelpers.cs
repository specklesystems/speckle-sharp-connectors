using System.Globalization;
using System.Text.RegularExpressions;
using Speckle.Objects.Geometry;

namespace Speckle.Converter.Navisworks.Helpers;

public static class PropertyHelpers
{
  private static readonly HashSet<string> s_excludedCategories = ["Geometry", "Metadata"];

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
      }
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

    // Default case for unsupported types
    return value.DataType == NAV.VariantDataType.None || value.DataType == NAV.VariantDataType.Point2D
      ? null
      : value.ToString();
  }

  /// <summary>
  /// Adds a property to an object (either a Base object or a Dictionary) if the value is not null or empty.
  /// </summary>
  /// <param name="baseObject">The object to which the property is to be added. Can be either a Base object or a Dictionary.</param>
  /// <param name="propertyName">The name of the property to add.</param>
  /// <param name="value">The value of the property.</param>
  internal static void AddPropertyIfNotNullOrEmpty(object baseObject, string propertyName, object? value)
  {
    switch (value)
    {
      case null:
        break; // Do not add null values
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

  /// <summary>
  /// Helper method to assign the property to the base object or dictionary.
  /// </summary>
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

  /// <summary>
  /// Sanitizes property names by replacing invalid characters with underscores.
  /// </summary>
  internal static string SanitizePropertyName(string name) =>
    // Regex pattern from speckle-sharp/Core/Core/Models/DynamicBase.cs IsPropNameValid
    name == "Item"
      // Item is a reserved term for Indexed Properties: https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/indexers/using-indexers
      ? "Item"
      : Regex.Replace(name, @"[\.\/\s]", "_");

  internal static bool IsCategoryToBeSkipped(NAV.PropertyCategory propertyCategory) =>
    s_excludedCategories.Contains(propertyCategory.DisplayName);
}
