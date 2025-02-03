using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Services;
using Speckle.Converters.RevitShared.Settings;

namespace Speckle.Converters.Revit2023.ToSpeckle.Properties;

public class StructuralMaterialAssetExtractor
{
  private readonly ScalingServiceToSpeckle _scalingService;
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;

  public StructuralMaterialAssetExtractor(
    ScalingServiceToSpeckle scalingService,
    IConverterSettingsStore<RevitConversionSettings> converterSettings
  )
  {
    _scalingService = scalingService;
    _converterSettings = converterSettings;
  }

  /// <summary>
  /// Extracts structural asset properties from a material's structural asset.
  /// </summary>
  /// <returns>Dictionary containing structural properties</returns>
  public void GetProperties(DB.ElementId structuralAssetId, Dictionary<string, object> properties)
  {
    if (structuralAssetId != DB.ElementId.InvalidElementId)
    {
      if (_converterSettings.Current.Document.GetElement(structuralAssetId) is DB.PropertySetElement propertySetElement)
      {
        DB.StructuralAsset structuralAsset = propertySetElement.GetStructuralAsset();

        properties["name"] = structuralAsset.Name;

        // TODO: simplify? lot of code just for one property
        // get unit forge type id
        DB.ForgeTypeId densityUnit = _converterSettings
          .Current.Document.GetUnits()
          .GetFormatOptions(DB.SpecTypeId.MassDensity)
          .GetUnitTypeId();

        // scale from internal to model units
        double densityValue = _scalingService.Scale(structuralAsset.Density, densityUnit);

        // write complete value
        properties["density"] = new Dictionary<string, object>
        {
          ["name"] = "density",
          ["value"] = densityValue,
          ["units"] = densityUnit
        };
      }
    }
  }
}
