using Microsoft.Extensions.Logging;
using Speckle.Converter.Navisworks.Settings;
using Speckle.Converters.Common;
using Speckle.Sdk.Models;
using static Speckle.Converter.Navisworks.Helpers.ElementSelectionHelper;

namespace Speckle.Converter.Navisworks.ToSpeckle;

public class DisplayValueExtractor
{
  private readonly IConverterSettingsStore<NavisworksConversionSettings> _converterSettings;
  private readonly ILogger<DisplayValueExtractor> _logger;
  private readonly GeometryToSpeckleConverter _geometryConverter;

  public DisplayValueExtractor(
    IConverterSettingsStore<NavisworksConversionSettings> converterSettings,
    ILogger<DisplayValueExtractor> logger
  )
  {
    _converterSettings = converterSettings;
    _logger = logger;
    _geometryConverter = new GeometryToSpeckleConverter(_converterSettings.Current);
  }

  internal List<Base> GetDisplayValue(NAV.ModelItem modelItem)
  {
    if (modelItem == null)
    {
      throw new ArgumentNullException(nameof(modelItem));
    }
    if (!modelItem.HasGeometry)
    {
      return [];
    }

    // Check if this is an instance definition by looking at InstanceHashCode
    bool isInstanceDefinition = modelItem.InstanceHashCode != 0;

    return !IsElementVisible(modelItem) ? [] : _geometryConverter.Convert(modelItem, isInstanceDefinition);
  }
}
