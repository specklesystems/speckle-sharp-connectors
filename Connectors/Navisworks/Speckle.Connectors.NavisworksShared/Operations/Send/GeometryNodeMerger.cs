using Speckle.Connector.Navisworks.Services;
using Speckle.Converter.Navisworks.Constants;

namespace Speckle.Connector.Navisworks.Operations.Send;

/// <summary>
/// Groups geometry nodes by their parent paths for merging displayValues
/// </summary>
public static class GeometryNodeMerger
{
  /// <summary>
  /// Groups sibling geometry nodes based on material properties for merging.
  /// Only merges nodes that share the same parent and have identical material properties.
  /// </summary>
  /// <param name="nodes">The collection of ModelItems to process</param>
  /// <returns>Dictionary mapping parent paths (with material signature suffix) to their mergeable child nodes</returns>
  public static Dictionary<string, List<NAV.ModelItem>> GroupSiblingGeometryNodes(IReadOnlyList<NAV.ModelItem> nodes)
  {
    // Group nameless geometry nodes by parent path and material signature
    var mergeableGroups = nodes
      .Where(node => node.HasGeometry && string.IsNullOrEmpty(node.DisplayName)) // Only anonymous geometry nodes
      .GroupBy(node =>
      {
        // Get parent path
        var service = new ElementSelectionService();
        var path = service.GetModelItemPath(node);
        var lastSeparatorIndex = path.LastIndexOf(PathConstants.SEPARATOR);
        var parentPath = lastSeparatorIndex == -1 ? path : path[..lastSeparatorIndex];

        // Combine parent path with material signature
        string materialSignature = GetMaterialSignature(node);
        return $"{parentPath}{PathConstants.MATERIAL_SEPARATOR}{materialSignature}";
      })
      .Where(group => group.Count() > 1) // Only include groups with multiple children
      .ToDictionary(group => group.Key, group => group.ToList());

    return mergeableGroups;
  }

  /// <summary>
  /// Creates a signature string that represents the material properties of a node.
  /// </summary>
  /// <param name="node">The ModelItem to create a material signature for</param>
  /// <returns>A signature string representing the node's material properties</returns>
  private static string GetMaterialSignature(NAV.ModelItem node)
  {
    if (!node.HasGeometry || node.Geometry == null)
    {
      return "nogeometry";
    }

    var geometry = node.Geometry;

    // Create a material signature based on active, permanent, and original colors and transparency
    return $"{geometry.ActiveColor?.GetHashCode() ?? 0}_{geometry.ActiveTransparency}_{geometry.PermanentColor?.GetHashCode() ?? 0}_{geometry.PermanentTransparency}_{geometry.OriginalColor?.GetHashCode() ?? 0}_{geometry.OriginalTransparency}";
  }

  /// <summary>
  /// Extracts just the parent path from a composite key that may include a material signature.
  /// </summary>
  /// <param name="compositeKey">The composite key which may include both path and material signature</param>
  /// <returns>The parent path portion of the key</returns>
  public static string GetParentPathFromKey(string compositeKey)
  {
    var separatorIndex = compositeKey.IndexOf(PathConstants.MATERIAL_SEPARATOR, StringComparison.Ordinal);
    return separatorIndex > 0 ? compositeKey[..separatorIndex] : compositeKey;
  }
}
