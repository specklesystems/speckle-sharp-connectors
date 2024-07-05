using Objects.BuiltElements.Revit;

namespace Speckle.Converters.RevitShared.Helpers;

/// <summary>
/// Contains predefined categories of revit objects as well as the types that make up the elements
/// in that category
/// </summary>
///
// POC: is quite a messy looking thing, need some review
// might be legit or maybe a bette/cleaner pattern rather than the dictionary of pain, but maybe not
// some whitespace between each at the least!
public static class RevitCategories
{
  /// <summary>
  /// Returns the corresponding <see cref="RevitCategory"/> based on a given built-in category name
  /// </summary>
  /// <param name="builtInCategory">The name of the built-in category</param>
  /// <returns>The RevitCategory enum value that corresponds to the given name</returns>
  public static RevitCategory GetSchemaBuilderCategoryFromBuiltIn(string builtInCategory)
  {
    // Clean up built-in name "OST_Walls" to be just "WALLS"
    var cleanName = builtInCategory
      .Replace("OST_IOS", "") //for OST_IOSModelGroups
      .Replace("OST_MEP", "") //for OST_MEPSpaces
      .Replace("OST_", "") //for any other OST_blablabla
      .Replace("_", " ");

    var res = Enum.TryParse(cleanName, out RevitCategory cat);
    if (!res)
    {
      throw new NotSupportedException($"Built-in category {builtInCategory} is not supported.");
    }

    return cat;
  }

  /// <summary>
  /// Returns the corresponding built-in category name from a specific <see cref="RevitCategory"/>
  /// </summary>
  /// <param name="c">The RevitCategory to convert</param>
  /// <returns>The name of the built-in category that corresponds to the input RevitCategory</returns>
  public static string GetBuiltInFromSchemaBuilderCategory(RevitCategory c)
  {
    var name = Enum.GetName(typeof(RevitCategory), c);
    return $"OST_{name}";
  }
}
