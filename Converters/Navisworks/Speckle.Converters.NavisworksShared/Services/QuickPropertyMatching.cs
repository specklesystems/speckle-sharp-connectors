using Speckle.Converter.Navisworks.Helpers;

namespace Speckle.Converter.Navisworks.Services;

internal static class QuickPropertyMatching
{
  internal static bool IncludesCategory(IReadOnlyList<QuickPropertyDefinition> definitions, string categoryDisplayName)
  {
    if (definitions.Count == 0 || string.IsNullOrWhiteSpace(categoryDisplayName))
    {
      return false;
    }

    foreach (var definition in definitions)
    {
      if (CategoryEquals(definition.CategoryDisplayName, categoryDisplayName))
      {
        return true;
      }
    }

    return false;
  }

  internal static bool IncludesProperty(
    IReadOnlyList<QuickPropertyDefinition> definitions,
    string categoryDisplayName,
    string propertyDisplayName
  )
  {
    if (definitions.Count == 0)
    {
      return false;
    }

    foreach (var definition in definitions)
    {
      if (
        CategoryEquals(definition.CategoryDisplayName, categoryDisplayName)
        && PropertyEquals(definition.PropertyDisplayName, propertyDisplayName)
      )
      {
        return true;
      }
    }

    return false;
  }

  internal static bool IncludesPropertyDisplayName(
    IReadOnlyList<QuickPropertyDefinition> definitions,
    string propertyDisplayName
  )
  {
    if (definitions.Count == 0 || string.IsNullOrWhiteSpace(propertyDisplayName))
    {
      return false;
    }

    foreach (var definition in definitions)
    {
      if (PropertyEquals(definition.PropertyDisplayName, propertyDisplayName))
      {
        return true;
      }
    }

    return false;
  }

  internal static bool IncludesItemRootProperty(
    IReadOnlyList<QuickPropertyDefinition> definitions,
    string sanitizedPropertyKey
  )
  {
    foreach (var definition in definitions)
    {
      if (!CategoryEquals(definition.CategoryDisplayName, "Item"))
      {
        continue;
      }

      if (
        PropertyEquals(definition.PropertyDisplayName, sanitizedPropertyKey)
        || string.Equals(SanitizePropertyName(definition.PropertyDisplayName), sanitizedPropertyKey, StringComparison.OrdinalIgnoreCase)
      )
      {
        return true;
      }
    }

    return false;
  }

  private static string SanitizePropertyName(string name) =>
    name == "Item" ? "Item" : Regex.Replace(name, @"[\.\/\s]", "_");

  private static bool CategoryEquals(string left, string right) =>
    string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

  private static bool PropertyEquals(string left, string right) =>
    string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
