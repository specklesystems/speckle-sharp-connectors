namespace Speckle.Converter.Navisworks.ToSpeckle.PropertyHandlers;

/// <summary>
/// Handles property assignment with hierarchy merging for objects that require ancestor properties.
/// </summary>
public class HierarchicalPropertyHandler(
  PropertySetsExtractor propertySetsExtractor,
  ModelPropertiesExtractor modelPropertiesExtractor,
  ClassPropertiesExtractor classPropertiesExtractor
) : BasePropertyHandler(propertySetsExtractor, modelPropertiesExtractor)
{
  private string PseudoClassPropertiesKey => "_pseudoClassProperties";

  public override Dictionary<string, object?> GetProperties(NAV.ModelItem modelItem)
  {
    var propertyDict = classPropertiesExtractor.GetClassProperties(modelItem) ?? new Dictionary<string, object?>();

    var hierarchy = GetObjectHierarchy(modelItem);
    var propertyCollection = new Dictionary<string, Dictionary<string, HashSet<object?>>>();

    foreach (var item in hierarchy)
    {
      CollectHierarchicalProperties(item, propertyCollection);
    }

    ApplyFilteredProperties(propertyDict, propertyCollection);
    FlattenPseudoClassProperties(propertyDict);

    return propertyDict;
  }

  private static List<NAV.ModelItem> GetObjectHierarchy(NAV.ModelItem target)
  {
    var hierarchy = new List<NAV.ModelItem>();
    var firstObjectAncestor = target.FindFirstObjectAncestor();

    if (firstObjectAncestor == null)
    {
      hierarchy.Add(target);
      return hierarchy;
    }

    var current = target;
    while (current != null)
    {
      hierarchy.Add(current);
      if (current == firstObjectAncestor)
      {
        break;
      }

      current = current.Parent;
    }

    hierarchy.Reverse();
    return hierarchy;
  }

  private void CollectHierarchicalProperties(
    NAV.ModelItem item,
    Dictionary<string, Dictionary<string, HashSet<object?>>> propertyCollection
  )
  {
    var categoryDictionaries = ProcessPropertySets(item);
    if (categoryDictionaries.Count == 0)
    {
      return;
    }

    foreach (var kvp in categoryDictionaries)
    {
      if (!propertyCollection.TryGetValue(kvp.Key, out var categoryProperties))
      {
        categoryProperties = [];
        propertyCollection.Add(kvp.Key, categoryProperties);
      }

      if (kvp.Value is not Dictionary<string, object?> properties)
      {
        // Get or create the pseudoClassProperties category
        if (!propertyCollection.TryGetValue(PseudoClassPropertiesKey, out var pseudoProperties))
        {
          pseudoProperties = [];
          propertyCollection.Add(PseudoClassPropertiesKey, pseudoProperties);
        }

        // Add this non-dictionary value to pseudoClassProperties
        if (!pseudoProperties.TryGetValue(kvp.Key, out var valueSet))
        {
          valueSet = [];
          pseudoProperties.Add(kvp.Key, valueSet);
        }

        valueSet.Add(kvp.Value);
        continue;
      }

      foreach (var prop in properties.Where(prop => IsValidPropertyValue(prop.Value)))
      {
        if (!categoryProperties.TryGetValue(prop.Key, out var valueSet))
        {
          valueSet = [];
          categoryProperties.Add(prop.Key, valueSet);
        }

        valueSet.Add(prop.Value);
      }
    }
  }

  private void FlattenPseudoClassProperties(Dictionary<string, object?> propertyDict)
  {
    string[] bannedNamesForProps =
    [
      "ClassName",
      "ClassDisplayName",
      "DisplayName",
      "InstanceGuid",
      "Source",
      "Source Guid"
    ];

    if (
      !propertyDict.TryGetValue(PseudoClassPropertiesKey, out var pseudoPropsObj)
      || pseudoPropsObj is not Dictionary<string, object> pseudoProps
    )
    {
      return;
    }

    // Add each pseudo class property to the main dictionary
    foreach (var prop in pseudoProps.Where(prop => !bannedNamesForProps.Contains(prop.Key)))
    {
      // if prop value is null, skip it
      if (prop.Value == null)
      {
        continue;
      }

      propertyDict[prop.Key] = prop.Value;
    }

    // Remove the pseudoClassProperties container
    propertyDict.Remove(PseudoClassPropertiesKey);
  }

  private static void ApplyFilteredProperties(
    Dictionary<string, object?> propertyDict,
    Dictionary<string, Dictionary<string, HashSet<object?>>> propertyCollection
  )
  {
    foreach (var kvp in propertyCollection)
    {
      var categoryDict = new Dictionary<string, object?>();
      bool hasProperties = false;

      foreach (var kvS in kvp.Value)
      {
        if ((kvS.Value).Count != 1)
        {
          continue;
        }

        categoryDict[kvS.Key] = kvS.Value.First();
        hasProperties = true;
      }

      if (hasProperties)
      {
        propertyDict[kvp.Key] = categoryDict;
      }
    }
  }
}
