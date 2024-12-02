using Microsoft.Extensions.Logging;
using Speckle.Converter.Navisworks.Settings;
using Speckle.Converters.Common;

namespace Speckle.Converter.Navisworks.ToSpeckle;

public class DisplayValueExtractor
{
  private readonly IConverterSettingsStore<NavisworksConversionSettings> _converterSettings;
  private readonly ILogger<DisplayValueExtractor> _logger;

  public DisplayValueExtractor(
    IConverterSettingsStore<NavisworksConversionSettings> converterSettings,
    ILogger<DisplayValueExtractor> logger
  )
  {
    _converterSettings = converterSettings;
    _logger = logger;
  }

  internal List<SSM.Base> GetDisplayValue(NAV.ModelItem _) => [];
}
