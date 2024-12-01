using Speckle.Converter.Navisworks.Settings;
using Speckle.Converters.Common;
using static Speckle.Converter.Navisworks.Helpers.PropertyHelpers;

namespace Speckle.Converter.Navisworks.ToSpeckle;

public class PropertySetsExtractor
{
  private readonly IConverterSettingsStore<NavisworksConversionSettings> _settingsStore;

  public PropertySetsExtractor(IConverterSettingsStore<NavisworksConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  internal Dictionary<string, object?>? GetPropertySets(NAV.ModelItem modelItem)
  {
    if (_settingsStore.Current.ExcludeProperties)
    {
      return null;
    }

    var propertyDictionary = ExtractPropertySets(modelItem);

    return propertyDictionary;
  }

  /// <summary>
  /// Extracts property sets from a NAV.ModelItem and adds them to a dictionary,
  /// PropertySets are specific to the host application source appended to Navisworks and therefore
  /// arbitrary in nature.
  /// </summary>
  /// <param name="modelItem">The NAV.ModelItem from which property sets are extracted.</param>
  /// <returns>A dictionary containing property sets of the modelItem.</returns>
  private Dictionary<string, object?> ExtractPropertySets(NAV.ModelItem modelItem)
  {
    var propertySetDictionary = new Dictionary<string, object?>();

    foreach (var propertyCategory in modelItem.PropertyCategories)
    {
      if (IsCategoryToBeSkipped(propertyCategory))
      {
        continue;
      }

      var propertySet = new Dictionary<string, object?>();

      foreach (var property in propertyCategory.Properties)
      {
        string sanitizedName = SanitizePropertyName(property.DisplayName);
        var propertyValue = ConvertPropertyValue(property.Value, _settingsStore.Current.SpeckleUnits);

        if (propertyValue != null)
        {
          propertySet[sanitizedName] = propertyValue;
        }
      }

      if (propertySet.Count <= 0)
      {
        continue;
      }

      string categoryName = SanitizePropertyName(propertyCategory.DisplayName);

      propertySetDictionary[categoryName] = propertySet;
    }

    return propertySetDictionary;
  }
}
