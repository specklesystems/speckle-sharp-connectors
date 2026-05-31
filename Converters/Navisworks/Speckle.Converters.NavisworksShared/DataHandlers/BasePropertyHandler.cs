using Speckle.Converter.Navisworks.Services;
using Speckle.Converter.Navisworks.Settings;
using Speckle.Converters.Common;

namespace Speckle.Converter.Navisworks.ToSpeckle.PropertyHandlers;

/// <summary>
/// Base property handler providing common functionality for property assignment.
/// </summary>
public abstract class BasePropertyHandler(
  PropertySetsExtractor propertySetsExtractor,
  ModelPropertiesExtractor modelPropertiesExtractor,
  InternalPropertiesExtractor internalPropertiesExtractor,
  IConverterSettingsStore<NavisworksConversionSettings> settingsStore,
  IQuickPropertyDefinitionsCache quickPropertyDefinitionsCache
) : IPropertyHandler
{
  private static readonly HashSet<string> s_standardItemRootExcludedPropertyKeys = new(StringComparer.OrdinalIgnoreCase)
  {
    "Hidden",
    "Required",
    "Internal_Type",
    "Internal Type",
    "Icon",
  };

  public abstract Dictionary<string, object?> GetProperties(NAV.ModelItem modelItem);

  protected Dictionary<string, object?> ProcessPropertySets(NAV.ModelItem modelItem)
  {
    var categorizedProperties = new Dictionary<string, object?>();
    var propertySets = propertySetsExtractor.GetPropertySets(modelItem);

    if (propertySets != null)
    {
      foreach (var category in propertySets.Where(c => c.Key != "Transform"))
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

          var excludeStandardItemNoise = settingsStore.Current.User.PropertyDetailLevel == PropertyDetailLevel.Standard;
          foreach (var prop in itemProps)
          {
            if (excludeStandardItemNoise && s_standardItemRootExcludedPropertyKeys.Contains(prop.Key))
            {
              continue;
            }

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
    AddInternalProperties(modelItem, categorizedProperties);
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

  private static Dictionary<string, object?> CreatePropertyDictionary(Dictionary<string, object?> properties) =>
    properties.Where(prop => IsValidPropertyValue(prop.Value)).ToDictionary(prop => prop.Key, prop => prop.Value);

  private void AddInternalProperties(NAV.ModelItem modelItem, Dictionary<string, object?> categorizedProperties)
  {
    if (settingsStore.Current.User.PropertyDetailLevel == PropertyDetailLevel.None)
    {
      categorizedProperties.Remove("Internal");
      return;
    }

    if (!settingsStore.Current.User.IncludeInternalProperties)
    {
      categorizedProperties.Remove("Internal");
      return;
    }

    var internalProperties = internalPropertiesExtractor.GetInternalProperties(modelItem);
    if (internalProperties == null || internalProperties.Count == 0)
    {
      categorizedProperties.Remove("Internal");
      return;
    }

    categorizedProperties["Internal"] = internalProperties;
  }

  protected static bool IsValidPropertyValue(object? value) => value != null && !string.IsNullOrEmpty(value.ToString());
}
