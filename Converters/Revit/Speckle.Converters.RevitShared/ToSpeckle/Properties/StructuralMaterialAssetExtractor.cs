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
  /// Gets the name of a structural asset and its corresponding density with units.
  /// </summary>
  /// <remarks>
  /// Density scaled from internal units to model units
  /// </remarks>
  public (string name, double density, DB.ForgeTypeId unitId)? GetProperties(DB.ElementId structuralAssetId)
  {
    // NOTE: assetId != DB.ElementId.InvalidElementId checked in calling method. Assuming a valid StructuralAssetId
    if (
      _converterSettings.Current.Document.GetElement(structuralAssetId) is not DB.PropertySetElement propertySetElement
    )
    {
      return null;
    }
    DB.StructuralAsset structuralAsset = propertySetElement.GetStructuralAsset();

    // get unit forge type id
    DB.ForgeTypeId densityUnitId = _converterSettings
      .Current.Document.GetUnits()
      .GetFormatOptions(DB.SpecTypeId.MassDensity)
      .GetUnitTypeId();

    // scale from internal to model units
    double densityValue = _scalingService.Scale(structuralAsset.Density, densityUnitId);

    // return value and units
    return (structuralAsset.Name, densityValue, densityUnitId);
  }
}
