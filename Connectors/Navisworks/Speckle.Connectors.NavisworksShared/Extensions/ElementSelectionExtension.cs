namespace Speckle.Connector.Navisworks.Extensions;

/// <summary>
/// Provides extension methods for working with Navisworks ModelItem selections.
/// </summary>
public static class ElementSelectionExtension
{
  /// <summary>
  /// Resolves a Navisworks <see cref="NAV.ModelItem"/> to its unique index path representation.
  /// </summary>
  /// <param name="modelItem">The model item to resolve.</param>
  /// <returns>
  /// A string representing the model item's path. The path includes the model index and
  /// a hierarchical path identifier, separated by the specified separator (e.g., "0.a.b").
  /// For root-level model items, only the model index is included.
  /// </returns>
  /// <exception cref="ArgumentNullException">Thrown if <paramref name="modelItem"/> is null.</exception>
  internal static string ResolveModelItemToIndexPath(NAV.ModelItem modelItem)
  {
    if (modelItem == null)
    {
      throw new ArgumentNullException(nameof(modelItem));
    }

    var modelItemPathId = NavisworksApp.ActiveDocument.Models.CreatePathId(modelItem);

    var pathIndex =
      modelItemPathId.PathId == "a"
        ? $"{modelItemPathId.ModelIndex}" // Root-level model item
        : $"{modelItemPathId.ModelIndex}{PathConstants.SEPARATOR}{modelItemPathId.PathId}"; // Nested model item

    return pathIndex;
  }

  internal static NAV.ModelItem ResolveIndexPathToModelItem(string indexPath)
  {
    var indexPathParts = indexPath.Split(PathConstants.SEPARATOR);

    var modelIndex = int.Parse(indexPathParts[0]);
    var pathId = string.Join(PathConstants.SEPARATOR.ToString(), indexPathParts.Skip(1));

    // assign the first part of indexPathParts to modelIndex and parse it to int, the second part to pathId string
    NAV.DocumentParts.ModelItemPathId modelItemPathId = new() { ModelIndex = modelIndex, PathId = pathId };

    var modelItem = NavisworksApp.ActiveDocument.Models.ResolvePathId(modelItemPathId);
    return modelItem;
  }

  /// <summary>
  /// Determines whether a Navisworks <see cref="NAV.ModelItem"/> and all its ancestors are visible.
  /// </summary>
  /// <param name="modelItem">The model item to check for visibility.</param>
  /// <returns>True if the item and all ancestors are visible; otherwise, false.</returns>
  /// <exception cref="ArgumentNullException">Thrown if <paramref name="modelItem"/> is null.</exception>
  internal static bool IsElementVisible(NAV.ModelItem modelItem)
  {
    if (modelItem == null)
    {
      throw new ArgumentNullException(nameof(modelItem));
    }

    // Check visibility status for the item and its ancestors
    return modelItem.AncestorsAndSelf.All(item => !item.IsHidden);
  }

  internal static List<NAV.ModelItem> ResolveGeometryLeafNodes(NAV.ModelItem modelItem) =>
    modelItem.DescendantsAndSelf.Where(x => x.HasGeometry).ToList();
}
