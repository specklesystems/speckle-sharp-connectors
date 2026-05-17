using System.Diagnostics;
using System.Text.Json;
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
  /// PropertySets are the specific set per host application source appended to Navisworks and therefore
  /// arbitrary in nature.
  /// </summary>
  /// <param name="modelItem">The NAV.ModelItem from which property sets are extracted.</param>
  /// <returns>A dictionary containing property sets of the modelItem.</returns>
  private Dictionary<string, object?> ExtractPropertySets(NAV.ModelItem modelItem)
  {
    var stopwatch = Stopwatch.StartNew();
    var propertySetDictionary = new Dictionary<string, object?>();
    var modelUnits = GetModelUnits(modelItem);
    var propertyDetailLevel = settingsStore.Current.User.PropertyDetailLevel;
    var userFilteredPropertyCategories = modelItem.GetUserFilteredPropertyCategories();
    var categories = propertyDetailLevel == PropertyDetailLevel.Full
      ? modelItem.PropertyCategories
      : userFilteredPropertyCategories;
    int totalCategoryCount = modelItem.PropertyCategories.Count();
    int userFilteredCategoryCount = userFilteredPropertyCategories.Count();
    int extractedPropertyCount = 0;

    propertyConverter.Reset();

    foreach (var propertyCategory in categories)
    {
      if (ShouldSkipCategory(propertyCategory))
      {
        continue;
      }

      if (propertyDetailLevel == PropertyDetailLevel.Essential && propertyCategory.DisplayName == "Material")
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
        extractedPropertyCount += propertySet.Count;
      }
    }

    stopwatch.Stop();
    int payloadBytes =
      propertySetDictionary.Count == 0 ? 0 : JsonSerializer.SerializeToUtf8Bytes(propertySetDictionary).Length;
    PropertyExtractionMetricsTracker.Record(
      totalCategoryCount,
      userFilteredCategoryCount,
      extractedPropertyCount,
      payloadBytes,
      stopwatch.Elapsed.TotalMilliseconds
    );

    return propertySetDictionary;
  }
}
