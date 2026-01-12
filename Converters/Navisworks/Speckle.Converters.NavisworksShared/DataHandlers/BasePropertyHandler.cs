namespace Speckle.Converter.Navisworks.ToSpeckle.PropertyHandlers;

/// <summary>
/// Base property handler providing common functionality for property assignment.
/// </summary>
public abstract class BasePropertyHandler(
  PropertySetsExtractor propertySetsExtractor,
  ModelPropertiesExtractor modelPropertiesExtractor
) : IPropertyHandler
{
  public abstract Dictionary<string, object?> GetProperties(NAV.ModelItem modelItem);
  private readonly List<string> _excludedProperties = ["Hidden", "Required", "Internal_Type"];

  protected Dictionary<string, object?> ProcessPropertySets(NAV.ModelItem modelItem)
  {
    var categorizedProperties = new Dictionary<string, object?>();
    var propertySets = propertySetsExtractor.GetPropertySets(modelItem);

    if (propertySets != null)
    {
      foreach (var category in propertySets.Where(c => c.Key is not "Internal" and not "Transform"))
      {
        if (category.Value is not Dictionary<string, object?> properties)
        {
          continue;
        }
        var itemProps = CreatePropertyDictionary(properties);

        if (category.Key == "Item")
        {
          if (itemProps.Count <= 0)
          {
            continue;
          }

          // add all non-excluded properties in the Item category to the root level
          foreach (var prop in itemProps.Where(prop => !_excludedProperties.Contains(prop.Key)))
          {
            categorizedProperties[prop.Key] = prop.Value;
          }
        }
        else
        {
          if (itemProps.Count > 0)
          {
            categorizedProperties[category.Key] = itemProps;
          }
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

  private void AddModelProperties(NAV.ModelItem modelItem, Dictionary<string, object?> categorizedProperties)
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
    // Most properties are valid, so use source capacity as hint to avoid resizing
    var propertyDict = new Dictionary<string, object?>(properties.Count);
    foreach (var prop in properties.Where(prop => IsValidPropertyValue(prop.Value)))
    {
      propertyDict[prop.Key] = prop.Value;
    }
    return propertyDict;
  }

  protected static bool IsValidPropertyValue(object? value) => value != null && !string.IsNullOrEmpty(value.ToString());
}
