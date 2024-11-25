namespace Speckle.Connector.Navisworks.Extensions;

/// <summary>
/// Provides extension methods for working with Navisworks ModelItem selections.
/// </summary>
public static class ElementSelectionExtension
{
  private const char DEFAULT_SEPARATOR = '.';

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

    return modelItemPathId.PathId == "a"
      ? $"{modelItemPathId.ModelIndex}" // Root-level model item
      : $"{modelItemPathId.ModelIndex}{DEFAULT_SEPARATOR}{modelItemPathId.PathId}"; // Nested model item
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
}
