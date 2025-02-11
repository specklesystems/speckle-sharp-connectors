using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Revit2023.ToSpeckle.Properties;
using Speckle.Converters.RevitShared.Services;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Sdk.Common;

namespace Speckle.Converters.RevitShared.ToSpeckle;

public readonly struct StructuralAssetProperties
{
  public string Name { get; init; }
  public double Density { get; init; }
  public DB.ForgeTypeId DensityUnitId { get; init; }
  public string MaterialType { get; init; }
  public double? CompressiveStrength { get; init; }
  public DB.ForgeTypeId? CompressiveStrengthUnitId { get; init; }

  public StructuralAssetProperties(
    string name,
    double density,
    DB.ForgeTypeId densityUnitId,
    string materialType,
    double? compressiveStrength,
    DB.ForgeTypeId? compressiveStrengthUnitId
  )
  {
    Name = name;
    Density = density;
    DensityUnitId = densityUnitId;
    MaterialType = materialType;
    CompressiveStrength = compressiveStrength;
    CompressiveStrengthUnitId = compressiveStrengthUnitId;
  }
}

/// <summary>
/// Lighter converter for material quantities. For each material quantity available on the target element, it will return a dictionary containing: area, volume, density, material name, material class and material category.
/// POC: we need to validate this with user needs. It currently ONLY includes density from the material parameters - any other more complex props were dropped to ensure speedy sending of data and a lighter payload.
/// We're keen to re-add more data though, provided we can validate it. If more props come, then switch to MaterialProxy needs to be looked at in more detail.
/// </summary>
public class MaterialQuantitiesToSpeckleLite : ITypedConverter<DB.Element, Dictionary<string, object>>
{
  private readonly Dictionary<string, StructuralAssetProperties> _structuralAssetCache = new();
  private readonly ScalingServiceToSpeckle _scalingService;
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private readonly StructuralMaterialAssetExtractor _structuralAssetExtractor;

  public MaterialQuantitiesToSpeckleLite(
    ScalingServiceToSpeckle scalingService,
    IConverterSettingsStore<RevitConversionSettings> converterSettings,
    StructuralMaterialAssetExtractor structuralAssetExtractor
  )
  {
    _scalingService = scalingService;
    _converterSettings = converterSettings;
    _structuralAssetExtractor = structuralAssetExtractor;
  }

  /// <summary>
  /// Lighter conversion of material quantities to speckle. For each material quantity available on the target element,
  /// it will return a dictionary containing: area, volume, density, material name, material class and material category.
  /// This conversion also manages a cache for the density retrieved from the material parameters.
  /// </summary>
  /// <param name="target"></param>
  /// <remarks>
  /// Request for densities => https://speckle.community/t/accessing-material-density-parameter-value/16026
  /// Since we're only extracting density from the material parameters, it's acceptable to attach to objects.
  /// If extracted material parameters grows, this will need to be relooked at!
  /// </remarks>
  public Dictionary<string, object> Convert(DB.Element target)
  {
    Dictionary<string, object> quantities = new();
    if (target.Category?.HasMaterialQuantities ?? false) //category can be null
    {
      foreach (DB.ElementId? matId in target.GetMaterialIds(false))
      {
        if (matId is null)
        {
          continue;
        }

        var materialQuantity = new Dictionary<string, object>();
        double factor = _scalingService.ScaleLength(1);
        var unitSettings = _converterSettings.Current.Document.GetUnits();

        AddMaterialProperty(
          materialQuantity,
          "area",
          factor * factor * target.GetMaterialArea(matId, false),
          unitSettings.GetFormatOptions(DB.SpecTypeId.Area).GetUnitTypeId()
        );

        AddMaterialProperty(
          materialQuantity,
          "volume",
          factor * factor * factor * target.GetMaterialVolume(matId),
          unitSettings.GetFormatOptions(DB.SpecTypeId.Volume).GetUnitTypeId()
        );

        if (_converterSettings.Current.Document.GetElement(matId) is DB.Material material)
        {
          materialQuantity["materialName"] = material.Name;
          materialQuantity["materialCategory"] = material.MaterialCategory;
          materialQuantity["materialClass"] = material.MaterialClass;

          // get StructuralAssetId
          DB.ElementId structuralAssetId = material.StructuralAssetId;
          if (structuralAssetId != DB.ElementId.InvalidElementId)
          {
            StructuralAssetProperties structuralAssetProperties = TryExtractStructuralAssetParameters(
              structuralAssetId
            );

            materialQuantity["structuralAsset"] = structuralAssetProperties.Name;
            AddMaterialProperty(
              materialQuantity,
              "density",
              structuralAssetProperties.Density,
              structuralAssetProperties.DensityUnitId
            );
            materialQuantity["materialType"] = structuralAssetProperties.MaterialType;

            // Only add compressive strength for concrete materials
            if (
              structuralAssetProperties.MaterialType == "Concrete"
              && structuralAssetProperties.CompressiveStrength.HasValue
            )
            {
              AddMaterialProperty(
                materialQuantity,
                "compressiveStrength",
                structuralAssetProperties.CompressiveStrength.Value,
                structuralAssetProperties.CompressiveStrengthUnitId!
              );
            }
          }

          quantities[material.Name] = materialQuantity;
        }
      }
    }

    return quantities;
  }

  private StructuralAssetProperties TryExtractStructuralAssetParameters(DB.ElementId assetId)
  {
    // ensure safe string conversion
    string assetIdString = assetId.ToString().NotNull();

    // check cache if density has already been extracted
    if (_structuralAssetCache.TryGetValue(assetIdString, out var cachedProperties))
    {
      return cachedProperties;
    }

    // if not in cache but structural asset id is valid => attempt extraction from StructuralMaterialAssertExtractor
    var extractedProperties = _structuralAssetExtractor.GetProperties(assetId);
    _structuralAssetCache[assetIdString] = extractedProperties;
    return extractedProperties;
  }

  /// <summary>
  /// Adds a material property to the given dictionary with standardized structure.
  /// </summary>
  /// <param name="materialQuantity">The dictionary to mutate with the new property</param>
  /// <param name="name">The name of the property (e.g., "area", "volume", "density")</param>
  /// <param name="value">The numeric value of the property</param>
  /// <param name="unitId">The Forge type ID representing the units of the property</param>
  /// <remarks>
  /// Etabs implements an extension method to dicts (see utils folder). May be worth exploring.
  /// </remarks>
  private void AddMaterialProperty(
    Dictionary<string, object> materialQuantity,
    string name,
    double value,
    DB.ForgeTypeId unitId
  )
  {
    materialQuantity[name] = new Dictionary<string, object>
    {
      ["name"] = name,
      ["value"] = value,
      ["units"] = DB.LabelUtils.GetLabelForUnit(unitId)
    };
  }
}
