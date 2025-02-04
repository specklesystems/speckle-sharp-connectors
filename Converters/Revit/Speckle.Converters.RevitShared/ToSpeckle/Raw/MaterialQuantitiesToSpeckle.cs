using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Revit2023.ToSpeckle.Properties;
using Speckle.Converters.RevitShared.Services;
using Speckle.Converters.RevitShared.Settings;

namespace Speckle.Converters.RevitShared.ToSpeckle;

/// <summary>
/// Lighter converter for material quantities. It basically returns a For each material quantity available on the target element, it will return a dictionary containing: area, volume, units, material name, material class and material category.
/// POC: we need to validate this with user needs. It currently does not include material parameters or any other more complex props to ensure speedy sending of data and a lighter payload. We're though keen to re-add more data provided we can validate it.
/// </summary>
public class MaterialQuantitiesToSpeckleLite : ITypedConverter<DB.Element, Dictionary<string, object>>
{
  private readonly Dictionary<string, (double density, string units)> _structuralAssetDensityCache = new();
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
  /// it will return a dictionary containing: area, volume, units, material name and material id.
  /// This conversion also manages material proxy creation in the cache for use by the root object builder.
  /// </summary>
  /// <param name="target"></param>
  /// <remarks>
  /// MaterialProxy and RenderMaterialProxy are completely separated in this regard.
  /// </remarks>
  public Dictionary<string, object> Convert(DB.Element target)
  {
    Dictionary<string, object> quantities = new();
    if (target.Category?.HasMaterialQuantities ?? false) //category can be null
    {
      foreach (DB.ElementId matId in target.GetMaterialIds(false))
      {
        if (matId is null)
        {
          continue;
        }

        var materialQuantity = new Dictionary<string, object>();

        double factor = _scalingService.ScaleLength(1);
        materialQuantity["area"] = factor * factor * target.GetMaterialArea(matId, false);
        materialQuantity["volume"] = factor * factor * factor * target.GetMaterialVolume(matId);
        materialQuantity["units"] = _converterSettings.Current.SpeckleUnits;

        if (_converterSettings.Current.Document.GetElement(matId) is DB.Material material)
        {
          materialQuantity["materialName"] = material.Name;
          materialQuantity["materialCategory"] = material.MaterialCategory;
          materialQuantity["materialClass"] = material.MaterialClass;

          var density = TryGetDensity(material);
          if (density.HasValue)
          {
            materialQuantity["density"] = new Dictionary<string, object>
            {
              ["name"] = "density",
              ["value"] = density.Value.density,
              ["units"] = density.Value.units
            };
          }

          quantities[material.Name] = materialQuantity;
        }
      }
    }

    return quantities;
  }

  private (double density, string units)? TryGetDensity(DB.Material material)
  {
    // get StructuralAssetId
    DB.ElementId assetId = material.StructuralAssetId;
    if (assetId == DB.ElementId.InvalidElementId)
    {
      return null; // no structural asset => early break
    }

    // check cache if density has already been extracted
    if (_structuralAssetDensityCache.TryGetValue(assetId.ToString(), out var cachedDensity))
    {
      return cachedDensity;
    }

    // if not in cache but structural asset id is valid => attempt extraction from StructuralMaterialAssertExtractor
    var extractedDensity = _structuralAssetExtractor.GetDensity(assetId);
    if (extractedDensity.HasValue)
    {
      _structuralAssetDensityCache[assetId.ToString()] = extractedDensity.Value;
      return extractedDensity;
    }

    return null;
  }
}
