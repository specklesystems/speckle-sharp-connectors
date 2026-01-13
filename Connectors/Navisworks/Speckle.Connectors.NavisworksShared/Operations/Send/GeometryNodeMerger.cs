using Speckle.Connector.Navisworks.Services;
using static Speckle.Converter.Navisworks.Constants.PathConstants;

namespace Speckle.Connector.Navisworks.Operations.Send;

/// <summary>
/// Groups geometry nodes by their parent paths for merging displayValues
/// </summary>
public static class GeometryNodeMerger
{
  /// <summary>
  /// Groups sibling geometry nodes based on material properties for merging.
  /// This only merges nodes that share the same parent and have identical material properties.
  /// </summary>
  /// <param name="nodes">The collection of ModelItems to process</param>
  /// <returns>Dictionary mapping parent paths (with material signature suffix) to their mergeable child nodes</returns>
  public static Dictionary<string, List<NAV.ModelItem>> GroupSiblingGeometryNodes(IReadOnlyList<NAV.ModelItem> nodes)
  {
    var selectionService = new ConnectorElementSelectionService();

    // Group nameless geometry nodes by parent path and material signature
    var mergeableGroups = nodes
      .Where(node => node.HasGeometry && string.IsNullOrEmpty(node.DisplayName)) // Only anonymous geometry nodes
      .GroupBy(node =>
      {
        // Get the parent path
        var path = selectionService.GetModelItemPath(node);
        var lastSeparatorIndex = path.LastIndexOf(SEPARATOR);
        var parentPath = lastSeparatorIndex == -1 ? path : path[..lastSeparatorIndex];

        // Generate material signature
        string signature = GenerateSignature(node);

        // Combine parent path with signature
        return $"{parentPath}{MATERIAL_SEPARATOR}{signature}";
      })
      .Where(group => group.Count() > 1) // Only include groups with multiple children
      .ToDictionary(group => group.Key, group => group.ToList());

    return mergeableGroups;
  }

  /// <summary>
  /// Generates a signature for a node based on material properties
  /// </summary>
  private static string GenerateSignature(NAV.ModelItem node)
  {
    var signatureProperties = new Dictionary<string, object>();

    // We can as many signature defining methods as we want here
    AddMaterialProperties(node, signatureProperties);

    // When we are done adding properties, we can generate the signature
    return GetSignature(signatureProperties);
  }

  /// <summary>
  /// Adds material-related properties to the properties dictionary
  /// </summary>
  private static void AddMaterialProperties(NAV.ModelItem node, Dictionary<string, object> properties)
  {
    if (!node.HasGeometry || node.Geometry == null)
    {
      return;
    }

    var geometry = node.Geometry;
    if (geometry.ActiveColor != null)
    {
      properties["ActiveColor"] = (geometry.ActiveColor.R, geometry.ActiveColor.G, geometry.ActiveColor.B);
      properties["ActiveTransparency"] = geometry.ActiveTransparency;
    }

    // Add material name if available
    var materialName = GetMaterialName(node);
    if (!string.IsNullOrEmpty(materialName))
    {
      properties["MaterialName"] = materialName;
    }
  }

  /// <summary>
  /// Creates a hash-based signature from a dictionary of properties.
  /// </summary>
  /// <param name="properties">Dictionary containing property name/value pairs to include in the signature</param>
  /// <param name="hashLength">Length of the returned hash string (default: 8 characters)</param>
  /// <returns>A hash string representing the combined properties</returns>
  private static string GetSignature(Dictionary<string, object> properties, int hashLength = 8)
  {
    if (properties.Count == 0)
    {
      return "empty";
    }

    // Build a consistent string representation of all properties
    var hashInput = new System.Text.StringBuilder();

    // Sort keys to ensure a consistent order
    var sortedKeys = properties.Keys.OrderBy(k => k).ToList();

    foreach (var key in sortedKeys)
    {
      var value = properties[key];
      switch (value)
      {
        case null:
          continue;
        // Format numbers with fixed precision to avoid floating point inconsistencies
        case double doubleValue:
          hashInput.Append($"{key}:{Math.Round(doubleValue, 6)}_");
          break;
        case float floatValue:
          hashInput.Append($"{key}:{Math.Round(floatValue, 6)}_");
          break;
        default:
          hashInput.Append($"{key}:{value.GetHashCode()}_");
          break;
      }
    }

    if (hashInput.Length == 0)
    {
      return "empty";
    }

    // Use MD5 hash with warning suppression
#pragma warning disable CA5351 // Do Not Use Broken Cryptographic Algorithms
    using var md5 = System.Security.Cryptography.MD5.Create();
    var inputBytes = System.Text.Encoding.UTF8.GetBytes(hashInput.ToString());
    var hashBytes = md5.ComputeHash(inputBytes);
#pragma warning restore CA5351

    var fullHashString = BitConverter.ToString(hashBytes).Replace("-", "");
    return fullHashString[..Math.Min(hashLength, fullHashString.Length)];
  }

  /// <summary>
  /// Extracts material name from a node if available.
  /// </summary>
  private static string GetMaterialName(NAV.ModelItem node)
  {
    // Check the Item category for material name
    var itemCategory = node.PropertyCategories.FindCategoryByDisplayName("Item");
    if (itemCategory != null)
    {
      var itemProperties = itemCategory.Properties;
      var itemMaterial = itemProperties.FindPropertyByDisplayName("Material");
      if (itemMaterial != null && !string.IsNullOrEmpty(itemMaterial.DisplayName))
      {
        return itemMaterial.Value.ToDisplayString();
      }
    }

    // Check Material category for material name
    var materialPropertyCategory = node.PropertyCategories.FindCategoryByDisplayName("Material");
    if (materialPropertyCategory == null)
    {
      return string.Empty;
    }

    var material = materialPropertyCategory.Properties;
    var name = material.FindPropertyByDisplayName("Name");
    return name != null && !string.IsNullOrEmpty(name.DisplayName) ? name.Value.ToDisplayString() : string.Empty;
  }
}
