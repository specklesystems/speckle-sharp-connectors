namespace Speckle.Converter.Navisworks.Helpers;

/// <summary>
/// Helper class for generating display paths by traversing meaningful Navisworks model item ancestors.
/// </summary>
public static class DisplayPathHelper
{
  private const char PATH_DELIMITER = '>';

  /// <summary>
  /// Generates a display path string for a ModelItem by traversing its meaningful ancestors.
  /// </summary>
  public static string GenerateDisplayPath(NAV.ModelItem modelItem)
  {
    if (modelItem == null)
    {
      throw new ArgumentNullException(nameof(modelItem));
    }

    var ancestorPath = new List<string>();

    // Start with the root document name if available
    if (modelItem.HasModel && !string.IsNullOrEmpty(modelItem.Model.FileName))
    {
      ancestorPath.Add(Path.GetFileNameWithoutExtension(modelItem.Model.FileName));
    }

    // Work backwards through ancestors, skipping empty or geometry nodes
    var current = modelItem.Parent;
    while (current != null)
    {
      // Skip nodes without meaningful names or geometry nodes
      if (!string.IsNullOrEmpty(current.DisplayName) && !IsGeometryNode(current))
      {
        // Don't add duplicate names in sequence
        if (ancestorPath.Count == 0 || ancestorPath.Last() != current.DisplayName)
        {
          ancestorPath.Add(current.DisplayName);
        }
      }

      current = current.Parent;
    }

    // Reverse to get root->leaf order
    ancestorPath.Reverse();
    // if (ancestorPath.Count > 1)
    // {
    //   ancestorPath.RemoveAt(ancestorPath.Count - 1);
    // }

    return string.Join($" {PATH_DELIMITER} ", ancestorPath);
  }

  private static bool IsGeometryNode(NAV.ModelItem item)
  {
    // A geometry node typically has geometry but no meaningful display name
    return item.HasGeometry && string.IsNullOrEmpty(item.DisplayName);
  }
}
