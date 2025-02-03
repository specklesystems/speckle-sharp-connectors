using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Settings;

namespace Speckle.Converters.Revit2023.ToSpeckle.Properties;

public class ThermalMaterialAssetExtractor
{
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;

  public ThermalMaterialAssetExtractor(IConverterSettingsStore<RevitConversionSettings> converterSettings)
  {
    _converterSettings = converterSettings;
  }

  /// <summary>
  /// Extracts thermal asset properties from a material's thermal asset.
  /// </summary>
  /// <returns>Dictionary containing thermal properties</returns>
  public Dictionary<string, object> GetProperties(DB.ElementId thermalAssetId)
  {
    var properties = new Dictionary<string, object>();

    var thermalAsset = _converterSettings.Current.Document.GetElement(thermalAssetId);
    properties["Name"] = thermalAsset.Name;

    return properties;
  }
}
