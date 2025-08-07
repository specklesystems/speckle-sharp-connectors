using static Speckle.Converter.Navisworks.Helpers.PropertyHelpers;

namespace Speckle.Converter.Navisworks.ToSpeckle;

public static class RevitBuiltInCategoryExtractor
{
  private const int ANCESTOR_AND_SELF_COUNT = 4;

  /// <summary>
  /// Searches modelItem hierarchy for Revit category and adds mapped built-in category to the dictionary
  /// </summary>
  internal static void AddRevitCategoryFromHierarchy(
    NAV.ModelItem modelItem,
    Dictionary<string, object?> propertyDictionary
  )
  {
    var categoryValue = FindRevitCategoryInHierarchy(modelItem);
    if (categoryValue == null)
    {
      return;
    }
    var convertedValue = ConvertPropertyValue(categoryValue, "")?.ToString() ?? string.Empty;
    var builtInCategory = DisplayNameToRevitBuiltInCategory(convertedValue);

    if (builtInCategory == convertedValue)
    {
      // If the category is not mapped, we don't add it to the dictionary
      // return;
    }

    AddPropertyIfNotNullOrEmpty(propertyDictionary, "builtInCategory", builtInCategory);
  }

  // Traverses up to 4 levels of model hierarchy because, god dammit, Navisworks explodes those Revit elements.
  private static IEnumerable<NAV.ModelItem> GetHierarchyItems(NAV.ModelItem modelItem)
  {
    var current = modelItem;
    for (int i = 0; i < ANCESTOR_AND_SELF_COUNT && current != null; i++, current = current.Parent)
    {
      yield return current;
    }
  }

  /// <summary>
  /// Finds the Revit category in the hierarchy of modelItem.
  /// Early exit if the category is found.
  /// </summary>
  private static NAV.VariantData? FindRevitCategoryInHierarchy(NAV.ModelItem modelItem)
  {
    var current = modelItem;
    for (int i = 0; i < ANCESTOR_AND_SELF_COUNT && current != null; i++, current = current.Parent)
    {
      var categoryValue = current
        .PropertyCategories.FindPropertyByName("LcRevitData_Element", "LcRevitPropertyElementCategory")
        ?.Value;
      if (categoryValue != null)
      {
        return categoryValue;
      }
    }
    return null;
  }

  // Maps Navisworks display names to Revit OST constants
  // TODO: This mapping should be extended to cover all Revit categories and stored in a more maintainable way
  private static string DisplayNameToRevitBuiltInCategory(string displayName) =>
    displayName switch
    {
      "Walls" => "OST_Walls",
      "Floors" => "OST_Floors",
      "Supports" or "Runs" => "OST_Stairs",
      "Doors" => "OST_Doors",
      "Windows" => "OST_Windows",
      "Columns" => "OST_Columns",
      "Casework" => "OST_Casework",
      "Ceilings" => "OST_Ceilings",
      "Curtain Panels" => "OST_CurtainWallPanels",
      "Curtain Wall Mullions" => "OST_CurtainWallMullions",
      "Roofs" => "OST_Roofs",
      "Air Terminals" => "OST_DuctTerminal",
      _ => displayName
    };
}
