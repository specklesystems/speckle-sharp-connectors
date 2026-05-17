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
  private static readonly HashSet<string> s_essentialClassProperties = new(StringComparer.Ordinal)
  {
    "InstanceGuid",
    "DisplayName",
    "ClassName",
  };

  public override Dictionary<string, object?> GetProperties(NAV.ModelItem modelItem)
  {
    var properties = ProcessPropertySets(modelItem);
    var classProperties = classPropertiesExtractor.GetClassProperties(modelItem);
    if (classProperties.Count == 0)
    {
      return properties;
    }

    var propertyDetailLevel = settingsStore.Current.User.PropertyDetailLevel;
    if (propertyDetailLevel == PropertyDetailLevel.Essential)
    {
      foreach (var kvp in classProperties)
      {
        if (!s_essentialClassProperties.Contains(kvp.Key))
        {
          continue;
        }

        properties[kvp.Key] = kvp.Value;
      }

      return properties;
    }

    foreach (var kvp in classProperties)
    {
      properties[kvp.Key] = kvp.Value;
    }

    return properties;
  }
}
