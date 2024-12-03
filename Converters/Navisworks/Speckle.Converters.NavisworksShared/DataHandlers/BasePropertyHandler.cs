﻿namespace Speckle.Converter.Navisworks.ToSpeckle.PropertyHandlers;

/// <summary>
/// Base property handler providing common functionality for property assignment.
/// </summary>
public abstract class BasePropertyHandler(
  ClassPropertiesExtractor classPropertiesExtractor,
  PropertySetsExtractor propertySetsExtractor,
  ModelPropertiesExtractor modelPropertiesExtractor
) : IPropertyHandler
{
  public void AssignProperties(SSM.Base speckleObject, NAV.ModelItem modelItem)
  {
    AssignClassProperties(speckleObject, modelItem);
    AssignPropertySets(speckleObject, modelItem);
  }

  private void AssignClassProperties(SSM.Base speckleObject, NAV.ModelItem modelItem)
  {
    var classProperties = classPropertiesExtractor.GetClassProperties(modelItem);
    if (classProperties == null)
    {
      return;
    }

    foreach (var kvp in classProperties)
    {
      if (speckleObject != null)
      {
        speckleObject[kvp.Key] = kvp.Value;
      }
    }
  }

  protected abstract void AssignPropertySets(SSM.Base speckleObject, NAV.ModelItem modelItem);

  protected Dictionary<string, Dictionary<string, object?>> ProcessPropertySets(NAV.ModelItem modelItem)
  {
    var categorizedProperties = new Dictionary<string, Dictionary<string, object?>>();
    var propertySets = propertySetsExtractor.GetPropertySets(modelItem);

    if (propertySets != null)
    {
      foreach (var category in propertySets.Where(c => c.Key != "Internal"))
      {
        if (category.Value is not Dictionary<string, object?> properties)
        {
          continue;
        }

        var categoryProps = CreatePropertyDictionary(properties);
        if (categoryProps.Count > 0)
        {
          categorizedProperties[category.Key] = categoryProps;
        }
      }
    }

    if (modelItem == null)
    {
      throw new ArgumentNullException(nameof(modelItem));
    }

    AddModelProperties(modelItem, categorizedProperties);
    return categorizedProperties;
  }

  private void AddModelProperties(
    NAV.ModelItem modelItem,
    Dictionary<string, Dictionary<string, object?>> categorizedProperties
  )
  {
    if (!modelItem.HasModel)
    {
      return;
    }

    var modelProperties = modelPropertiesExtractor.GetModelProperties(modelItem.Model);
    if (modelProperties == null)
    {
      return;
    }

    var modelProps = CreatePropertyDictionary(modelProperties);
    if (modelProps.Count > 0)
    {
      categorizedProperties["Model"] = modelProps;
    }
  }

  private static Dictionary<string, object?> CreatePropertyDictionary(Dictionary<string, object?> properties)
  {
    var propertyDict = new Dictionary<string, object?>();
    foreach (var prop in properties.Where(prop => IsValidPropertyValue(prop.Value)))
    {
      propertyDict[prop.Key] = prop.Value;
    }
    return propertyDict;
  }

  protected static bool IsValidPropertyValue(object? value) => value != null && !string.IsNullOrEmpty(value.ToString());
}
