using Speckle.Converter.Navisworks.Settings;
using Speckle.Converters.Common;

namespace Speckle.Converter.Navisworks.ToSpeckle;

public class PropertySetsExtractor
{
  private readonly IConverterSettingsStore<NavisworksConversionSettings> _settingsStore;

  public PropertySetsExtractor(IConverterSettingsStore<NavisworksConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  public Dictionary<string, object?>? GetPropertySets(NAV.ModelItem modelItem)
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
  private static Dictionary<string, object?> ExtractPropertySets(NAV.ModelItem modelItem)
  {
    var propertySetDictionary = new Dictionary<string, object?>();

    modelItem.PropertyCategories.ForEach(propertyCategory =>
    {
      var propertySet = new Dictionary<string, object?>();

      propertyCategory.Properties.ForEach(property =>
      {
        propertySet.Add(property.Name, property.Value);
      });

      propertySetDictionary.Add(propertyCategory.Name, propertySet);
    });

    return propertySetDictionary;
  }
}
