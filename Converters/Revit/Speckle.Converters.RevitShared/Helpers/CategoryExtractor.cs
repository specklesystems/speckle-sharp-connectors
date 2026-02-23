using Speckle.Objects.Data;
using Speckle.Sdk.Models;

namespace Speckle.Converters.RevitShared.Helpers;

/// <summary>
/// Responsible for extracting Revit BuiltInCategories.
/// </summary>
public static class CategoryExtractor
{
  public static string? ExtractBuiltInCategory(DataObject? parentDataObject, Base atomicObject)
  {
    // Try parent DataObject first (for InstanceProxy displayValue case)
    if (parentDataObject?.properties.TryGetValue("builtInCategory", out var cat) == true)
    {
      return cat?.ToString();
    }

    // Fallback to atomicObject properties
    if (
      atomicObject["properties"] is Dictionary<string, object?> props
      && props.TryGetValue("builtInCategory", out var fallbackCat)
    )
    {
      return fallbackCat?.ToString();
    }

    return null;
  }
}
