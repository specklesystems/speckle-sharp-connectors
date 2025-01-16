namespace Speckle.Converter.Navisworks.Helpers;

/// <summary>
/// Helper class for generating display paths by traversing Navisworks model item ancestors.
/// </summary>
public static class DisplayPathHelper
{
  private const char PATH_DELIMITER = '>';

  /// <summary>
  /// Generates a display path string for a ModelItem by traversing its ancestors up to the first object ancestor.
  /// </summary>
  /// <param name="modelItem">The ModelItem to generate a path for.</param>
  /// <returns>A string representing the full path of display names from ancestors, stopping at the first object.</returns>
  public static string GenerateDisplayPath(NAV.ModelItem modelItem)
  {
    if (modelItem == null)
    {
      throw new ArgumentNullException(nameof(modelItem));
    }

    var firstObjectAncestor = modelItem.FindFirstObjectAncestor();

    // If we don't find a first object ancestor, return just the model item's display name
    if (firstObjectAncestor == null)
    {
      return modelItem.DisplayName ?? string.Empty;
    }

    var current = firstObjectAncestor;
    var ancestorPath = new List<string>();

    // Work backwards from the first object ancestor to the root
    while (current != null)
    {
      if (!string.IsNullOrEmpty(current.DisplayName))
      {
        ancestorPath.Add(current.DisplayName);
      }
      current = current.Parent;
    }

    // Build the path from root to first object (reverse the collected names)
    ancestorPath.Reverse();
    return string.Join($" {PATH_DELIMITER} ", ancestorPath);
  }
}
