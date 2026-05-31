using Speckle.Converter.Navisworks.Helpers;
using Speckle.Converter.Navisworks.Services;
using Speckle.Converter.Navisworks.Settings;
using Speckle.Converters.Common;

namespace Speckle.Converter.Navisworks.ToSpeckle.PropertyHandlers;

/// <summary>
/// Handles standard property assignment without any merging or hierarchy processing.
/// </summary>
public class StandardPropertyHandler(
  PropertySetsExtractor propertySetsExtractor,
  ModelPropertiesExtractor modelPropertiesExtractor,
  InternalPropertiesExtractor internalPropertiesExtractor,
  ClassPropertiesExtractor classPropertiesExtractor,
  IConverterSettingsStore<NavisworksConversionSettings> settingsStore,
  IQuickPropertyDefinitionsCache quickPropertyDefinitionsCache
) : BasePropertyHandler(
    propertySetsExtractor,
    modelPropertiesExtractor,
    internalPropertiesExtractor,
    settingsStore,
    quickPropertyDefinitionsCache
  )
{
  private readonly IConverterSettingsStore<NavisworksConversionSettings> _settingsStore = settingsStore;

  public override Dictionary<string, object?> GetProperties(NAV.ModelItem modelItem)
  {
    var properties = ProcessPropertySets(modelItem);

    if (_settingsStore.Current.User.PropertyDetailLevel == PropertyDetailLevel.None)
    {
      return properties;
    }

    var classProperties = classPropertiesExtractor.GetClassProperties(modelItem);
    if (classProperties.Count == 0)
    {
      return properties;
    }

    var quickPropertyDefinitions = quickPropertyDefinitionsCache.Ensure();
    foreach (var kvp in classProperties)
    {
      if (
        PropertyHelpers.ShouldSkipProperty(
          kvp.Key,
          string.Empty,
          PropertyDetailLevel.Standard,
          quickPropertyDefinitions
        )
      )
      {
        continue;
      }

      properties[kvp.Key] = kvp.Value;
    }

    return properties;
  }
}
