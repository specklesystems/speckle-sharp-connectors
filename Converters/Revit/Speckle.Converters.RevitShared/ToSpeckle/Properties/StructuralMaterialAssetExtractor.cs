using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Services;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Sdk;
using Speckle.Sdk.Common;

namespace Speckle.Converters.Revit2023.ToSpeckle.Properties;

public sealed record  StructuralAssetProperties(
  string Name,
  double Density,
  DB.ForgeTypeId DensityUnitId,
  string MaterialType,
  double? CompressiveStrength,
  DB.ForgeTypeId? CompressiveStrengthUnitId
);

public class StructuralMaterialAssetExtractor(
  ScalingServiceToSpeckle scalingService,
  IConverterSettingsStore<RevitConversionSettings> converterSettings)
{
  private readonly Dictionary<string, StructuralAssetProperties> _structuralAssetCache = new();

  /// <summary>
  /// Attempts to get structural asset properties, using cached values if available.
  /// </summary>
  public StructuralAssetProperties TryGetProperties(DB.ElementId assetId)
  {
    // ensure safe string conversion
    string assetIdString = assetId.ToString().NotNull();

    // check cache if properties have already been extracted
    if (_structuralAssetCache.TryGetValue(assetIdString, out var cachedProperties))
    {
      return cachedProperties;
    }

    // if not in cache but structural asset id is valid => extract properties
    var extractedProperties = ExtractProperties(assetId);
    _structuralAssetCache[assetIdString] = extractedProperties;
    return extractedProperties;
  }

  /// <summary>
  /// Gets the material properties from a structural asset including density, material type,
  /// and material-specific properties like compressive strength for concrete.
  /// </summary>
  /// <remarks>
  /// All values are scaled from internal units to model units
  /// </remarks>
  private StructuralAssetProperties ExtractProperties(DB.ElementId structuralAssetId)
  {
    // NOTE: assetId != DB.ElementId.InvalidElementId checked in calling method. Assuming a valid StructuralAssetId
    if (
      converterSettings.Current.Document.GetElement(structuralAssetId) is not DB.PropertySetElement propertySetElement
    )
    {
      throw new SpeckleException("Structural material asset is not of expected type.");
    }
    DB.StructuralAsset structuralAsset = propertySetElement.GetStructuralAsset();

    // get unit forge type id
    DB.ForgeTypeId densityUnitId = converterSettings
      .Current.Document.GetUnits()
      .GetFormatOptions(DB.SpecTypeId.MassDensity)
      .GetUnitTypeId();

    // scale from internal to model units
    double densityValue = scalingService.Scale(structuralAsset.Density, densityUnitId);

    // get material type
    string materialType = structuralAsset.StructuralAssetClass.ToString();

    // initialize optional concrete properties
    double? compressiveStrength = null;
    DB.ForgeTypeId? stressUnitId = null;

    // if concrete, extract compressive strength
    if (materialType == DB.StructuralAssetClass.Concrete.ToString())
    {
      stressUnitId = converterSettings
        .Current.Document.GetUnits()
        .GetFormatOptions(DB.SpecTypeId.AreaForce)
        .GetUnitTypeId();

      compressiveStrength = scalingService.Scale(structuralAsset.ConcreteCompression, stressUnitId);
    }

    // return value and units
    return new StructuralAssetProperties(
   structuralAsset.Name,
      densityValue,
  densityUnitId,
  materialType,
compressiveStrength,
stressUnitId
    );
  }
}
