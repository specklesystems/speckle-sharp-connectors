using Speckle.Converter.Navisworks.Services;
using Speckle.Converter.Navisworks.Settings;
using Speckle.Converters.Common;
using static Speckle.Converter.Navisworks.Helpers.PropertyHelpers;

namespace Speckle.Converter.Navisworks.ToSpeckle;

public class PropertySetsExtractor(
  IConverterSettingsStore<NavisworksConversionSettings> settingsStore,
  IPropertyConverter propertyConverter
)
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

  private static NAV.Units GetModelUnits(NAV.ModelItem modelItem)
  {
    NAV.ModelItem? ancestor = modelItem;
    while (ancestor != null && !ancestor.HasModel)
    {
      ancestor = ancestor.Parent;
    }

    return ancestor != null ? ancestor.Model.Units : NAV.Units.Meters;
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
    var modelUnits = GetModelUnits(modelItem);

    propertyConverter.Reset();

    foreach (var propertyCategory in modelItem.PropertyCategories)
    {
      if (IsCategoryToBeSkipped(propertyCategory))
      {
        continue;
      }

      var propertySet = new Dictionary<string, object?>();

      foreach (var property in propertyCategory.Properties)
      {
        var sanitizedName = SanitizePropertyName(property.DisplayName);
        var propertyValue = propertyConverter.ConvertPropertyValue(property.Value, modelUnits, property.DisplayName);
        if (propertyValue != null)
        {
          propertySet[sanitizedName] = propertyValue;
        }
      }

      if (propertySet.Count > 0)
      {
        propertySetDictionary[SanitizePropertyName(propertyCategory.DisplayName)] = propertySet;
      }
    }

    return propertySetDictionary;
  }
}
