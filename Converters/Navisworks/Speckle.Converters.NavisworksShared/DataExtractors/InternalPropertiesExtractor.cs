using static Speckle.Converter.Navisworks.Helpers.PropertyHelpers;

namespace Speckle.Converter.Navisworks.ToSpeckle;

public class InternalPropertiesExtractor
{
  private static readonly string[] s_allowedKeys =
  [
    "ClassDisplayName",
    "ClassName",
    "DisplayName",
    "InstanceGuid",
    "Source",
    "Source Guid",
    "NodeType",
  ];

  internal Dictionary<string, object?>? GetInternalProperties(NAV.ModelItem modelItem)
  {
    if (modelItem == null)
    {
      throw new ArgumentNullException(nameof(modelItem));
    }

    var internalProperties = new Dictionary<string, object?>();

    AddPropertyIfNotNullOrEmpty(internalProperties, "ClassDisplayName", modelItem.ClassDisplayName);
    AddPropertyIfNotNullOrEmpty(internalProperties, "ClassName", modelItem.ClassName);
    AddPropertyIfNotNullOrEmpty(internalProperties, "DisplayName", modelItem.DisplayName);
    AddPropertyIfNotNullOrEmpty(
      internalProperties,
      "InstanceGuid",
      modelItem.InstanceGuid != Guid.Empty ? modelItem.InstanceGuid : null
    );
    AddPropertyIfNotNullOrEmpty(internalProperties, "Source", modelItem.Model?.SourceFileName);
    AddPropertyIfNotNullOrEmpty(
      internalProperties,
      "Source Guid",
      modelItem.Model?.SourceGuid != Guid.Empty ? modelItem.Model?.SourceGuid : null
    );
    AddPropertyIfNotNullOrEmpty(internalProperties, "NodeType", ResolveNodeType(modelItem));

    var filtered = new Dictionary<string, object?>();
    foreach (var key in s_allowedKeys)
    {
      if (internalProperties.TryGetValue(key, out var value) && value != null)
      {
        filtered[key] = value;
      }
    }

    return filtered.Count > 0 ? filtered : null;
  }

  private static string? ResolveNodeType(NAV.ModelItem modelItem)
  {
    if (modelItem.IsCollection)
    {
      return "Collection";
    }
    if (modelItem.IsComposite)
    {
      return "Composite Object";
    }
    if (modelItem.IsInsert)
    {
      return "Geometry Insert";
    }
    return modelItem.IsLayer ? "Layer" : null;
  }
}
