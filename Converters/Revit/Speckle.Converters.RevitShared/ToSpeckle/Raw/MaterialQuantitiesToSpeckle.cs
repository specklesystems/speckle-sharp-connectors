using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Services;
using Speckle.Converters.RevitShared.Settings;

namespace Speckle.Converters.RevitShared.ToSpeckle;

/// <summary>
/// Lighter converter for material quantities. It basically returns a For each material quantity available on the target element, it will return a dictionary containing: area, volume, units, material name, material class and material category.
/// POC: we need to validate this with user needs. It currently does not include material parameters or any other more complex props to ensure speedy sending of data and a lighter payload. We're though keen to re-add more data provided we can validate it.
/// </summary>
public class MaterialQuantitiesToSpeckleLite : ITypedConverter<DB.Element, Dictionary<string, object>>
{
  private readonly ScalingServiceToSpeckle _scalingService;
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;

  public MaterialQuantitiesToSpeckleLite(
    ScalingServiceToSpeckle scalingService,
    IConverterSettingsStore<RevitConversionSettings> converterSettings
  )
  {
    _scalingService = scalingService;
    _converterSettings = converterSettings;
  }

  /// <summary>
  /// Lighter conversion of material quantities to speckle. For each material quantity available on the target element, it will return a dictionary containing: area, volume, units, material name, material class and material category.
  /// </summary>
  /// <param name="target"></param>
  /// <returns></returns>
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
          quantities[material.Name] = materialQuantity;
        }
      }
    }

    return quantities;
  }
}
