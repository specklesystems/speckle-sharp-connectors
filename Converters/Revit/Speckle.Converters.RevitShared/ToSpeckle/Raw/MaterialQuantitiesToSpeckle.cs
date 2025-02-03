using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Revit2023.ToSpeckle.Properties;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Services;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Sdk.Models.Proxies;

namespace Speckle.Converters.RevitShared.ToSpeckle;

/// <summary>
/// Lighter converter for material quantities. It basically returns a For each material quantity available on the target element, it will return a dictionary containing: area, volume, units, material name, material class and material category.
/// POC: we need to validate this with user needs. It currently does not include material parameters or any other more complex props to ensure speedy sending of data and a lighter payload. We're though keen to re-add more data provided we can validate it.
/// </summary>
public class MaterialQuantitiesToSpeckleLite : ITypedConverter<DB.Element, Dictionary<string, object>>
{
  private readonly ScalingServiceToSpeckle _scalingService;
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private readonly RevitToSpeckleCacheSingleton _revitToSpeckleCacheSingleton;
  private readonly StructuralMaterialAssetExtractor _structuralAssetExtractor;
  private readonly ThermalMaterialAssetExtractor _thermalAssetExtractor;

  public MaterialQuantitiesToSpeckleLite(
    ScalingServiceToSpeckle scalingService,
    IConverterSettingsStore<RevitConversionSettings> converterSettings,
    RevitToSpeckleCacheSingleton revitToSpeckleCacheSingleton,
    StructuralMaterialAssetExtractor structuralAssetExtractor,
    ThermalMaterialAssetExtractor thermalAssetExtractor
  )
  {
    _scalingService = scalingService;
    _converterSettings = converterSettings;
    _revitToSpeckleCacheSingleton = revitToSpeckleCacheSingleton;
    _structuralAssetExtractor = structuralAssetExtractor;
    _thermalAssetExtractor = thermalAssetExtractor;
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
          materialQuantity["materialId"] = material.Id.ToString();
          quantities[material.Name] = materialQuantity;

          CreateOrUpdateMaterialProxy(material, target.Id.ToString());
        }
      }
    }

    return quantities;
  }

  private void CreateOrUpdateMaterialProxy(DB.Material material, string elementId)
  {
    var materialProxiesMap = _revitToSpeckleCacheSingleton.MaterialProxiesMap;
    string materialIdString = material.Id.ToString();

    if (!materialProxiesMap.TryGetValue(materialIdString, out var materialProxy))
    {
      Dictionary<string, object> materialAssetProperties = new();

      DB.ElementId structuralAssetId = material.StructuralAssetId;
      DB.ElementId thermalAssetId = material.ThermalAssetId;

      if (structuralAssetId != DB.ElementId.InvalidElementId)
      {
        materialAssetProperties["Structural"] = _structuralAssetExtractor.GetProperties(structuralAssetId);
      }

      if (thermalAssetId != DB.ElementId.InvalidElementId)
      {
        materialAssetProperties["Thermal"] = _thermalAssetExtractor.GetProperties(thermalAssetId);
      }

      materialProxy = new GroupProxy
      {
        applicationId = materialIdString,
        name = material.Name,
        objects = new List<string>(),
        ["category"] = material.MaterialCategory,
        ["class"] = material.MaterialClass,
        ["properties"] = materialAssetProperties,
      };

      materialProxiesMap[materialIdString] = materialProxy;
    }

    if (!materialProxy.objects.Contains(elementId))
    {
      materialProxy.objects.Add(elementId);
    }
  }
}
