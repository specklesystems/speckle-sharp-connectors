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
  IConverterSettingsStore<NavisworksConversionSettings> settingsStore
) : BasePropertyHandler(propertySetsExtractor, modelPropertiesExtractor, internalPropertiesExtractor, settingsStore)
{
  public override Dictionary<string, object?> GetProperties(NAV.ModelItem modelItem) => ProcessPropertySets(modelItem);
}
