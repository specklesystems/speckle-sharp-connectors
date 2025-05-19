namespace Speckle.Converter.Navisworks.Helpers;

/// <summary>
/// Helper class for extracting hierarchical context from Navisworks model items in a single traversal.
/// </summary>
public static class HierarchyHelper
{
  private const char PATH_DELIMITER = '>';

  /// <summary>
  /// Extracts the meaningful name and path from a ModelItem in a single traversal.
  /// </summary>
  public static (string name, string path) ExtractContext(NAV.ModelItem modelItem)
  {
    if (modelItem == null)
    {
      throw new ArgumentNullException(nameof(modelItem));
    }

    var ancestors = new List<string>();
    var meaningfulName = string.Empty;
    var current = modelItem;

    // Start with the root document name if available
    if (modelItem.HasModel && !string.IsNullOrEmpty(modelItem.Model.FileName))
    {
      ancestors.Add(System.IO.Path.GetFileNameWithoutExtension(modelItem.Model.FileName));
    }

    // Traverse up the tree once, collecting both name and path information
    while (current != null)
    {
      if (IsMeaningfulNode(current))
      {
        // First meaningful name we find is our object name (if we haven't found one yet)
        if (string.IsNullOrEmpty(meaningfulName))
        {
          meaningfulName = current.DisplayName;
        }
        // Add to ancestors if it's not a duplicate
        else if (ancestors.Count == 0 || ancestors.Last() != current.DisplayName)
        {
          ancestors.Add(current.DisplayName);
        }
      }
      current = current.Parent;
    }

    // Build path excluding the name we found
    ancestors.Reverse();

    var path = string.Join($" {PATH_DELIMITER} ", ancestors);

    return (meaningfulName, path);
  }

  private static bool IsMeaningfulNode(NAV.ModelItem item) =>
    !string.IsNullOrEmpty(item.DisplayName) && (!item.HasGeometry || !string.IsNullOrEmpty(item.ClassDisplayName));
}
