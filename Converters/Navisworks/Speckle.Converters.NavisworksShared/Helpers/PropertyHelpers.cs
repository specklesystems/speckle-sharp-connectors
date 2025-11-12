using System.Text.RegularExpressions;

namespace Speckle.Converter.Navisworks.Helpers;

public static class PropertyHelpers
{
  private static readonly HashSet<string> s_excludedCategories = ["Geometry", "Metadata"];

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
      _ => "Metres"
    };

  internal static string Area(NAV.Units u) => $"Square {Linear(u).ToLower()}";

  public static string Volume(NAV.Units u) => $"Cubic {Linear(u).ToLower()}";
}
