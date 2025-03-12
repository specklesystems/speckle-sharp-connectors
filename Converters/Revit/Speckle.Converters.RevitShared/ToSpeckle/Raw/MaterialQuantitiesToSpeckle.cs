using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Revit2023.ToSpeckle.Properties;
using Speckle.Converters.RevitShared.Services;
using Speckle.Converters.RevitShared.Settings;

namespace Speckle.Converters.RevitShared.ToSpeckle;

/// <summary>
/// Lighter converter for material quantities.
/// </summary>
/// <remarks>
/// We need to validate this with user needs. Currently limited to:
/// <list type="bullet">
///     <item><description>material category</description></item>
///     <item><description>material class</description></item>
///     <item><description>material name</description></item>
///     <item><description>area</description></item>
///     <item><description>volume</description></item>
///     <item><description>density (if valid StructuralAssetId)</description></item>
///     <item><description>type (if valid StructuralAssetId)</description></item>
///     <item><description>concrete compressive strength (if valid StructuralAssetId and of type concrete)</description></item>
/// </list>
/// We're attaching density, type and concrete compression (if concrete) to all objects. This is still "lite". If we add
/// more structural asset properties we should move to a proxy approach.
/// </remarks>
public class MaterialQuantitiesToSpeckleLite : ITypedConverter<DB.Element, Dictionary<string, object>>
{
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
        var unitSettings = _converterSettings.Current.Document.GetUnits();

        var areaUnitType = unitSettings.GetFormatOptions(DB.SpecTypeId.Area).GetUnitTypeId();
        AddMaterialProperty(
          materialQuantity,
          "area",
          _scalingService.Scale(target.GetMaterialArea(matId, false), areaUnitType),
          areaUnitType
        );

        var volumeUnitType = unitSettings.GetFormatOptions(DB.SpecTypeId.Volume).GetUnitTypeId();
        AddMaterialProperty(
          materialQuantity,
          "volume",
          _scalingService.Scale(target.GetMaterialVolume(matId), volumeUnitType),
          volumeUnitType
        );

        if (_converterSettings.Current.Document.GetElement(matId) is DB.Material material)
        {
          materialQuantity["materialName"] = material.Name;
          materialQuantity["materialCategory"] = material.MaterialCategory;
          materialQuantity["materialClass"] = material.MaterialClass;

          // get StructuralAssetId (or try to)
          DB.ElementId structuralAssetId = material.StructuralAssetId;
          if (structuralAssetId != DB.ElementId.InvalidElementId)
          {
            StructuralAssetProperties structuralAssetProperties = _structuralAssetExtractor.TryGetProperties(
              structuralAssetId
            );

            materialQuantity["structuralAsset"] = structuralAssetProperties.Name;
            AddMaterialProperty(
              materialQuantity,
              "density",
              structuralAssetProperties.Density,
              structuralAssetProperties.DensityUnitId
            );

            // more reliable way of determining material type (wood/concrete/type) as it uses Revit enum
            // materialClass, materialCategory etc. are user string inputs
            materialQuantity["materialType"] = structuralAssetProperties.MaterialType;

            // Only add compressive strength for concrete materials (used by F+E for Automate)
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

  /// <summary>
  /// Adds a material property to the given dictionary with standardized structure.
  /// </summary>
  /// <param name="materialQuantity">The dictionary to mutate with the new property</param>
  /// <param name="name">The name of the property (e.g., "area", "volume", "density")</param>
  /// <param name="value">The numeric value of the property</param>
  /// <param name="unitId">The Forge type ID representing the units of the property</param>
  /// <remarks>
  /// Saves code when used repeatedbly. Etabs implements an extension method to dicts (see utils folder). May be worth exploring.
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
