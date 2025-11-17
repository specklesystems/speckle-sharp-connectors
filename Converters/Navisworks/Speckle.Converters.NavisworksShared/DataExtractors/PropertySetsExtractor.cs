using Speckle.Converter.Navisworks.Settings;
using Speckle.Converters.Common;
using static Speckle.Converter.Navisworks.Helpers.PropertyHelpers;

namespace Speckle.Converter.Navisworks.ToSpeckle;

public class PropertySetsExtractor(IConverterSettingsStore<NavisworksConversionSettings> settingsStore)
{
  internal Dictionary<string, object?>? GetPropertySets(NAV.ModelItem modelItem)
  {
    if (settingsStore.Current.User.ExcludeProperties)
    {
      return null;
    }

    var propertyDictionary = ExtractPropertySets(modelItem);

    return propertyDictionary;
  }

  private Dictionary<string, object?> ExtractPropertySets(NAV.ModelItem modelItem)
  {
    var propertySetDictionary = new Dictionary<string, object?>();

    foreach (var propertyCategory in modelItem.PropertyCategories)
    {
      if (ShouldSkipCategory(propertyCategory))
      {
        continue;
      }

      var propertySet = new Dictionary<string, object?>();

      foreach (var property in propertyCategory.Properties)
      {
        string sanitizedName = SanitizePropertyName(property.DisplayName);
        var propertyValue = ConvertPropertyValue(property.Value, settingsStore.Current.Derived.SpeckleUnits);

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
