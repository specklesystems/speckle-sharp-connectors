using Speckle.Converter.Navisworks.Helpers;
using Speckle.Converter.Navisworks.Services;
using Speckle.Converter.Navisworks.Settings;
using Speckle.Converters.Common;

namespace Speckle.Converter.Navisworks.ToSpeckle.PropertyHandlers;

/// <summary>
/// Handles property assignment with hierarchy merging for objects that require ancestor properties.
/// </summary>
public class HierarchicalPropertyHandler(
  PropertySetsExtractor propertySetsExtractor,
  ModelPropertiesExtractor modelPropertiesExtractor,
  InternalPropertiesExtractor internalPropertiesExtractor,
  ClassPropertiesExtractor classPropertiesExtractor,
  IConverterSettingsStore<NavisworksConversionSettings> settingsStore,
  IRevitBuiltInCategoryExtractor revitCategoryExtractor,
  IQuickPropertyDefinitionsCache quickPropertyDefinitionsCache,
  IModelItemPropertySetsCache modelItemPropertySetsCache
)
  : BasePropertyHandler(
    propertySetsExtractor,
    modelPropertiesExtractor,
    internalPropertiesExtractor,
    settingsStore,
    quickPropertyDefinitionsCache,
    modelItemPropertySetsCache
  )
{
  private static string PseudoClassPropertiesKey => "_pseudoClassProperties";
  private static string InheritedPropertiesKey => "_inherited";
  private readonly bool _mapRevit = settingsStore.Current.User.RevitCategoryMapping;
  private readonly IConverterSettingsStore<NavisworksConversionSettings> _settingsStore = settingsStore;
  private IQuickPropertyDefinitionsCache _quickPropertyDefinitionsCache = quickPropertyDefinitionsCache;

  public override Dictionary<string, object?> GetProperties(NAV.ModelItem modelItem)
  {
    if (_settingsStore.Current.User.PropertyDetailLevel == PropertyDetailLevel.None)
    {
      return ProcessPropertySets(modelItem);
    }

    var propertyDict = classPropertiesExtractor.GetClassProperties(modelItem);
    if (_settingsStore.Current.User.PropertyDetailLevel == PropertyDetailLevel.Standard)
    {
      OmitStandardExcludedClassProperties(propertyDict, _quickPropertyDefinitionsCache.Ensure());
    }

    // Interop-lite mapping for Revit built-in categories
    if (_mapRevit && revitCategoryExtractor.TryGetBuiltInCategory(modelItem, out var builtInCategory))
    {
      PropertyHelpers.AddPropertyIfNotNullOrEmpty(
        propertyDict,
        RevitBuiltInCategoryExtractor.DEFAULT_DICT_KEY,
        builtInCategory
      );
    }

    var hierarchy = GetObjectHierarchy(modelItem);
    var propertyCollection = new Dictionary<string, Dictionary<string, List<object?>>>();

    foreach (var item in hierarchy)
    {
      CollectHierarchicalProperties(item, propertyCollection, storeInCache: item != modelItem);
    }

    ApplyFilteredProperties(propertyDict, propertyCollection);
    FlattenPseudoClassProperties(propertyDict);

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
    Dictionary<string, Dictionary<string, List<object?>>> propertyCollection,
    bool storeInCache
  )
  {
    var categoryDictionaries = ProcessPropertySets(item, storeInCache);
    if (categoryDictionaries.Count == 0)
    {
      return;
    }

    foreach (var kvp in categoryDictionaries)
    {
      if (kvp.Value is not Dictionary<string, object?> properties)
      {
        AppendValue(propertyCollection, PseudoClassPropertiesKey, kvp.Key, kvp.Value);
        continue;
      }

      foreach (var prop in properties.Where(prop => IsValidPropertyValue(prop.Value)))
      {
        AppendValue(propertyCollection, kvp.Key, prop.Key, prop.Value);
      }
    }
  }

  /// <summary>
  /// Appends a value to the ordered value list for a category/property pair. Callers walk the hierarchy from
  /// root to leaf, so the resulting list is in ancestor-first order.
  /// </summary>
  private static void AppendValue(
    Dictionary<string, Dictionary<string, List<object?>>> propertyCollection,
    string category,
    string propertyName,
    object? value
  )
  {
    if (!propertyCollection.TryGetValue(category, out var categoryProperties))
    {
      categoryProperties = [];
      propertyCollection.Add(category, categoryProperties);
    }

    if (!categoryProperties.TryGetValue(propertyName, out var values))
    {
      values = [];
      categoryProperties.Add(propertyName, values);
    }

    values.Add(value);
  }

  private static void FlattenPseudoClassProperties(Dictionary<string, object?> propertyDict)
  {
    string[] bannedNamesForProps =
    [
      "ClassName",
      "ClassDisplayName",
      "DisplayName",
      "InstanceGuid",
      "Source",
      "Source Guid",
    ];

    if (
      !propertyDict.TryGetValue(PseudoClassPropertiesKey, out var pseudoPropsObj)
      || pseudoPropsObj is not Dictionary<string, object> pseudoProps
    )
    {
      return;
    }

    foreach (var prop in pseudoProps.Where(prop => !bannedNamesForProps.Contains(prop.Key)))
    {
      if (prop.Value == null)
      {
        continue;
      }

      propertyDict[prop.Key] = prop.Value;
    }

    propertyDict.Remove(PseudoClassPropertiesKey);
  }

  /// <summary>
  /// Collapses the values gathered across the hierarchy into a single value per property. The most specific
  /// (leaf-most) value wins; any differing ancestor values are preserved under <see cref="InheritedPropertiesKey"/>
  /// rather than being discarded.
  /// </summary>
  private static void ApplyFilteredProperties(
    Dictionary<string, object?> propertyDict,
    Dictionary<string, Dictionary<string, List<object?>>> propertyCollection
  )
  {
    foreach (var kvp in propertyCollection)
    {
      var categoryDict = new Dictionary<string, object?>();
      var inherited = new Dictionary<string, object?>();

      foreach (var kvS in kvp.Value)
      {
        var values = kvS.Value;
        if (values.Count == 0)
        {
          continue;
        }

        // values are collected root -> leaf, so the last one is the item's own (or nearest ancestor's) value
        var effective = values[^1];
        categoryDict[kvS.Key] = effective;

        var ancestorValues = GetDistinctAncestorValues(values, effective);
        if (ancestorValues.Count > 0)
        {
          inherited[kvS.Key] = ancestorValues;
        }
      }

      if (categoryDict.Count == 0)
      {
        continue;
      }

      if (inherited.Count > 0)
      {
        categoryDict[InheritedPropertiesKey] = inherited;
      }

      propertyDict[kvp.Key] = categoryDict;
    }
  }

  /// <summary>
  /// Returns the distinct ancestor values that differ from the effective value, in root-to-leaf order.
  /// </summary>
  private static List<object?> GetDistinctAncestorValues(List<object?> values, object? effective)
  {
    var ancestorValues = new List<object?>();

    for (int i = 0; i < values.Count - 1; i++)
    {
      var value = values[i];
      if (Equals(value, effective) || ancestorValues.Any(existing => Equals(existing, value)))
      {
        continue;
      }

      ancestorValues.Add(value);
    }

    return ancestorValues;
  }

  private static void OmitStandardExcludedClassProperties(
    Dictionary<string, object?> propertyDict,
    IReadOnlyList<QuickPropertyDefinition> quickPropertyDefinitions
  )
  {
    string[] keys = propertyDict.Keys.ToArray();
    foreach (string key in keys)
    {
      if (PropertyHelpers.ShouldSkipProperty(key, string.Empty, PropertyDetailLevel.Standard, quickPropertyDefinitions))
      {
        propertyDict.Remove(key);
      }
    }
  }
}
