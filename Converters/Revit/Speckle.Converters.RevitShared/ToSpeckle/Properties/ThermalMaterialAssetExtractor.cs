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
  public void GetProperties(DB.ElementId thermalAssetId, Dictionary<string, object> properties)
  {
    if (thermalAssetId != DB.ElementId.InvalidElementId)
    {
      if (_converterSettings.Current.Document.GetElement(thermalAssetId) is DB.PropertySetElement propertySet)
      {
        DB.ThermalAsset thermalAsset = propertySet.GetThermalAsset();

        properties["name"] = thermalAsset.Name;
      }
    }
  }
}
