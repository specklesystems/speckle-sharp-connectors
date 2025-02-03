using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Settings;

namespace Speckle.Converters.Revit2023.ToSpeckle.Properties;

public class StructuralMaterialAssetExtractor
{
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;

  public StructuralMaterialAssetExtractor(IConverterSettingsStore<RevitConversionSettings> converterSettings)
  {
    _converterSettings = converterSettings;
  }

  /// <summary>
  /// Extracts structural asset properties from a material's structural asset.
  /// </summary>
  /// <returns>Dictionary containing structural properties</returns>
  public Dictionary<string, object> GetProperties(DB.ElementId structuralAssetId)
  {
    var properties = new Dictionary<string, object>();

    var structuralAsset = _converterSettings.Current.Document.GetElement(structuralAssetId);
    properties["Name"] = structuralAsset.Name;

    return properties;
  }
}
