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
  IQuickPropertyDefinitionsCache quickPropertyDefinitionsCache
)
  : BasePropertyHandler(
    propertySetsExtractor,
    modelPropertiesExtractor,
    internalPropertiesExtractor,
    settingsStore,
    quickPropertyDefinitionsCache
  )
{
  private static string PseudoClassPropertiesKey => "_pseudoClassProperties";
  private readonly bool _mapRevit = settingsStore.Current.User.RevitCategoryMapping;
  private readonly IConverterSettingsStore<NavisworksConversionSettings> _settingsStore = settingsStore;

  public override Dictionary<string, object?> GetProperties(NAV.ModelItem modelItem)
  {
    if (_settingsStore.Current.User.PropertyDetailLevel == PropertyDetailLevel.None)
    {
      return ProcessPropertySets(modelItem);
    }

    var propertyDict = classPropertiesExtractor.GetClassProperties(modelItem);
    if (_settingsStore.Current.User.PropertyDetailLevel == PropertyDetailLevel.Standard)
    {
      OmitStandardExcludedClassProperties(propertyDict, quickPropertyDefinitionsCache.Ensure());
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
    var propertyCollection = new Dictionary<string, Dictionary<string, HashSet<object?>>>();

    foreach (var item in hierarchy)
    {
      CollectHierarchicalProperties(item, propertyCollection);
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
    Dictionary<string, Dictionary<string, HashSet<object?>>> propertyCollection
  )
  {
    var categoryDictionaries = ProcessPropertySets(item);
    if (categoryDictionaries.Count == 0)
    {
      return;
    }

    foreach (var kvp in categoryDictionaries)
    {
      if (!propertyCollection.TryGetValue(kvp.Key, out var categoryProperties))
      {
        categoryProperties = [];
        propertyCollection.Add(kvp.Key, categoryProperties);
      }

      if (kvp.Value is not Dictionary<string, object?> properties)
      {
        if (!propertyCollection.TryGetValue(PseudoClassPropertiesKey, out var pseudoProperties))
        {
          pseudoProperties = [];
          propertyCollection.Add(PseudoClassPropertiesKey, pseudoProperties);
        }

        if (!pseudoProperties.TryGetValue(kvp.Key, out var valueSet))
        {
          valueSet = [];
          pseudoProperties.Add(kvp.Key, valueSet);
        }

        valueSet.Add(kvp.Value);
        continue;
      }

      foreach (var prop in properties.Where(prop => IsValidPropertyValue(prop.Value)))
      {
        if (!categoryProperties.TryGetValue(prop.Key, out var valueSet))
        {
          valueSet = [];
          categoryProperties.Add(prop.Key, valueSet);
        }

        valueSet.Add(prop.Value);
      }
    }
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

  private static void ApplyFilteredProperties(
    Dictionary<string, object?> propertyDict,
    Dictionary<string, Dictionary<string, HashSet<object?>>> propertyCollection
  )
  {
    foreach (var kvp in propertyCollection)
    {
      var categoryDict = new Dictionary<string, object?>();
      bool hasProperties = false;

      foreach (var kvS in kvp.Value.Where(kvS => kvS.Value.Count == 1))
      {
        categoryDict[kvS.Key] = kvS.Value.First();
        hasProperties = true;
      }

      if (hasProperties)
      {
        propertyDict[kvp.Key] = categoryDict;
      }
    }
  }

  private static void OmitStandardExcludedClassProperties(
    Dictionary<string, object?> propertyDict,
    IReadOnlyList<QuickPropertyDefinition> quickPropertyDefinitions
  )
  {
    string[] keys = propertyDict.Keys.ToArray();
    foreach (string key in keys)
    {
      if (
        PropertyHelpers.ShouldSkipProperty(key, string.Empty, PropertyDetailLevel.Standard, quickPropertyDefinitions)
      )
      {
        propertyDict.Remove(key);
      }
    }
  }
}
