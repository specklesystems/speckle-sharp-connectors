namespace Speckle.Converter.Navisworks.ToSpeckle.PropertyHandlers;

/// <summary>
/// Handles property assignment with hierarchy merging for objects that require ancestor properties.
/// </summary>
public class HierarchicalPropertyHandler(
  PropertySetsExtractor propertySetsExtractor,
  ModelPropertiesExtractor modelPropertiesExtractor
) : BasePropertyHandler(propertySetsExtractor, modelPropertiesExtractor)
{
  protected override void AssignPropertySets(SSM.Base speckleObject, NAV.ModelItem modelItem)
  {
    if (speckleObject == null)
    {
      throw new ArgumentNullException(nameof(speckleObject));
    }
    var propertyDict = speckleObject["properties"] as Dictionary<string, object?> ?? new Dictionary<string, object?>();

    if (modelItem == null)
    {
      throw new ArgumentNullException(nameof(modelItem));
    }

    var hierarchy = GetObjectHierarchy(modelItem);
    var propertyCollection = new Dictionary<string, Dictionary<string, HashSet<object?>>>();

    foreach (var item in hierarchy)
    {
      CollectHierarchicalProperties(item, propertyCollection);
    }

    ApplyFilteredProperties(propertyDict, propertyCollection);

    speckleObject["properties"] = propertyDict;
  }

  public override Dictionary<string, object?> GetProperties(NAV.ModelItem modelItem)
  {
    var propertyDict = new Dictionary<string, object?>();

    var hierarchy = GetObjectHierarchy(modelItem);
    var propertyCollection = new Dictionary<string, Dictionary<string, HashSet<object?>>>();

    foreach (var item in hierarchy)
    {
      CollectHierarchicalProperties(item, propertyCollection);
    }

    ApplyFilteredProperties(propertyDict, propertyCollection);

    var classProperties = ClassPropertiesExtractor.GetClassProperties(modelItem);

    // for each of the the keys of classProperties, add them to the propertyDict
    if (classProperties == null)
    {
      return propertyDict;
    }

    foreach (var kvp in classProperties)
    {
      if (propertyDict.ContainsKey(kvp.Key))
      {
        propertyDict[kvp.Key] = kvp.Value;
      }
      else
      {
        propertyDict.Add(kvp.Key, kvp.Value);
      }
    }

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

    foreach (var kvp in categoryDictionaries)
    {
      if (!propertyCollection.TryGetValue(kvp.Key, out var categoryProperties))
      {
        categoryProperties = [];
        propertyCollection.Add(kvp.Key, categoryProperties);
      }

      if (kvp.Value is not Dictionary<string, object?> properties)
      {
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
