using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Services;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Converters.RevitShared.ToSpeckle;
using Speckle.Sdk;

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
  /// Gets the material properties from a structural asset including density, material type,
  /// and material-specific properties like compressive strength for concrete.
  /// </summary>
  /// <remarks>
  /// All values are scaled from internal units to model units
  /// </remarks>
  public StructuralAssetProperties GetProperties(DB.ElementId structuralAssetId)
  {
    // NOTE: assetId != DB.ElementId.InvalidElementId checked in calling method. Assuming a valid StructuralAssetId
    if (
      _converterSettings.Current.Document.GetElement(structuralAssetId) is not DB.PropertySetElement propertySetElement
    )
    {
      throw new SpeckleException("Structural material asset is not of expected type.");
    }
    DB.StructuralAsset structuralAsset = propertySetElement.GetStructuralAsset();

    // get unit forge type id
    DB.ForgeTypeId densityUnitId = _converterSettings
      .Current.Document.GetUnits()
      .GetFormatOptions(DB.SpecTypeId.MassDensity)
      .GetUnitTypeId();

    // scale from internal to model units
    double densityValue = _scalingService.Scale(structuralAsset.Density, densityUnitId);

    // get material type
    string materialType = structuralAsset.StructuralAssetClass.ToString();

    // initialize optional concrete properties
    double? compressiveStrength = null;
    DB.ForgeTypeId? stressUnitId = null;

    // if concrete, extract compressive strength
    if (materialType == "Concrete")
    {
      stressUnitId = _converterSettings
        .Current.Document.GetUnits()
        .GetFormatOptions(DB.SpecTypeId.AreaForce)
        .GetUnitTypeId();

      compressiveStrength = _scalingService.Scale(structuralAsset.ConcreteCompression, stressUnitId);
    }

    // return value and units
    return new StructuralAssetProperties(
      name: structuralAsset.Name,
      density: densityValue,
      densityUnitId: densityUnitId,
      materialType: materialType,
      compressiveStrength: compressiveStrength,
      compressiveStrengthUnitId: stressUnitId
    );
  }
}
