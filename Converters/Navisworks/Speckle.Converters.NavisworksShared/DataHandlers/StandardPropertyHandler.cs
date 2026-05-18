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
  IConverterSettingsStore<NavisworksConversionSettings> settingsStore
) : BasePropertyHandler(propertySetsExtractor, modelPropertiesExtractor, internalPropertiesExtractor, settingsStore)
{
  private readonly IConverterSettingsStore<NavisworksConversionSettings> _settingsStore = settingsStore;

  public override Dictionary<string, object?> GetProperties(NAV.ModelItem modelItem)
  {
    if (_settingsStore.Current.User.PropertyDetailLevel == PropertyDetailLevel.None)
    {
      return [];
    }

    var properties = ProcessPropertySets(modelItem);
    var classProperties = classPropertiesExtractor.GetClassProperties(modelItem);
    if (classProperties.Count == 0)
    {
      return properties;
    }

    foreach (var kvp in classProperties)
    {
      properties[kvp.Key] = kvp.Value;
    }

    return properties;
  }
}
