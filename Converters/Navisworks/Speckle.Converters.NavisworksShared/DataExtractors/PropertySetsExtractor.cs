using System.Diagnostics;
using System.Text;
using Speckle.Converter.Navisworks.Services;
using Speckle.Converter.Navisworks.Settings;
using Speckle.Converters.Common;
using Speckle.Newtonsoft.Json;
using static Speckle.Converter.Navisworks.Helpers.PropertyHelpers;

namespace Speckle.Converter.Navisworks.ToSpeckle;

public class PropertySetsExtractor(
  IConverterSettingsStore<NavisworksConversionSettings> settingsStore,
  IPropertyConverter propertyConverter,
  IQuickPropertyDefinitionsCache quickPropertyDefinitionsCache
)
{
  internal Dictionary<string, object?>? GetPropertySets(NAV.ModelItem modelItem)
  {
    if (settingsStore.Current.User.PropertyDetailLevel == PropertyDetailLevel.None)
    {
      return ExtractItemPropertySets(modelItem);
    }

    return ExtractPropertySets(modelItem);
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

  private Dictionary<string, object?> ExtractPropertySets(NAV.ModelItem modelItem)
  {
    var propertyDetailLevel = settingsStore.Current.User.PropertyDetailLevel;
    var categories = modelItem.GetUserFilteredPropertyCategories();
    var quickPropertyDefinitions = quickPropertyDefinitionsCache.Ensure();

    return ExtractFromCategories(
      modelItem,
      categories,
      propertyDetailLevel,
      quickPropertyDefinitions,
      categories.Count()
    );
  }

  private Dictionary<string, object?> ExtractItemPropertySets(NAV.ModelItem modelItem)
  {
    var propertySetDictionary = new Dictionary<string, object?>();
    NAV.PropertyCategory? itemCategory = modelItem.PropertyCategories.FindCategoryByDisplayName("Item");
    if (itemCategory == null)
    {
      RecordMetrics(modelItem, 0, 0, propertySetDictionary, 0);
      return propertySetDictionary;
    }

    var stopwatch = Stopwatch.StartNew();
    var modelUnits = GetModelUnits(modelItem);
    var propertySet = ExtractPropertiesFromCategory(itemCategory, modelUnits, PropertyDetailLevel.None, []);

    if (propertySet.Count > 0)
    {
      propertySetDictionary["Item"] = propertySet;
    }

    stopwatch.Stop();
    RecordMetrics(
      modelItem,
      1,
      propertySet.Count,
      propertySetDictionary,
      stopwatch.Elapsed.TotalMilliseconds
    );

    return propertySetDictionary;
  }

  private Dictionary<string, object?> ExtractFromCategories(
    NAV.ModelItem modelItem,
    IEnumerable<NAV.PropertyCategory> categories,
    PropertyDetailLevel propertyDetailLevel,
    IReadOnlyList<QuickPropertyDefinition> quickPropertyDefinitions,
    int categoryCountForMetrics
  )
  {
    var stopwatch = Stopwatch.StartNew();
    var propertySetDictionary = new Dictionary<string, object?>();
    var modelUnits = GetModelUnits(modelItem);
    int extractedPropertyCount = 0;

    propertyConverter.Reset();

    foreach (var propertyCategory in categories)
    {
      if (ShouldSkipCategory(propertyCategory, propertyDetailLevel, quickPropertyDefinitions))
      {
        continue;
      }

      var propertySet = ExtractPropertiesFromCategory(
        propertyCategory,
        modelUnits,
        propertyDetailLevel,
        quickPropertyDefinitions
      );

      if (propertySet.Count > 0)
      {
        propertySetDictionary[SanitizePropertyName(propertyCategory.DisplayName)] = propertySet;
        extractedPropertyCount += propertySet.Count;
      }
    }

    stopwatch.Stop();
    RecordMetrics(
      modelItem,
      categoryCountForMetrics,
      extractedPropertyCount,
      propertySetDictionary,
      stopwatch.Elapsed.TotalMilliseconds
    );

    return propertySetDictionary;
  }

  private Dictionary<string, object?> ExtractPropertiesFromCategory(
    NAV.PropertyCategory propertyCategory,
    NAV.Units modelUnits,
    PropertyDetailLevel propertyDetailLevel,
    IReadOnlyList<QuickPropertyDefinition> quickPropertyDefinitions
  )
  {
    var propertySet = new Dictionary<string, object?>();
    string categoryDisplayName = propertyCategory.DisplayName ?? string.Empty;

    foreach (var property in propertyCategory.Properties)
    {
      if (
        ShouldSkipProperty(
          property.DisplayName,
          categoryDisplayName,
          propertyDetailLevel,
          quickPropertyDefinitions
        )
      )
      {
        continue;
      }

      var sanitizedName = SanitizePropertyName(property.DisplayName);
      var propertyValue = propertyConverter.ConvertPropertyValue(property.Value, modelUnits, property.DisplayName);
      if (propertyValue != null)
      {
        propertySet[sanitizedName] = propertyValue;
      }
    }

    return propertySet;
  }

  private static void RecordMetrics(
    NAV.ModelItem modelItem,
    int quickOrFilteredCategoryCount,
    int extractedPropertyCount,
    Dictionary<string, object?> propertySetDictionary,
    double extractionMs
  )
  {
    int payloadBytes =
      propertySetDictionary.Count == 0
        ? 0
        : Encoding.UTF8.GetByteCount(JsonConvert.SerializeObject(propertySetDictionary));
    PropertyExtractionMetricsTracker.Record(
      modelItem.PropertyCategories.Count(),
      quickOrFilteredCategoryCount,
      extractedPropertyCount,
      payloadBytes,
      extractionMs
    );
  }
}
