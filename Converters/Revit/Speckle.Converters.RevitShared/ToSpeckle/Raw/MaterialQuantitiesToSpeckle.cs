using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Services;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Objects.Other;
using Speckle.Objects.Other.Revit;

namespace Speckle.Converters.RevitShared.ToSpeckle;

/// <summary>
/// Lighter converter for material quantities. It basically returns a For each material quantity available on the target element, it will return a dictionary containing: area, volume, units, material name, material class and material category.
/// POC: we need to validate this with user needs. It currently does not include material parameters or any other more complex props to ensure speedy sending of data and a lighter payload. We're though keen to re-add more data provided we can validate it.
/// </summary>
public class MaterialQuantitiesToSpeckleLite : ITypedConverter<DB.Element, List<Dictionary<string, object>>>
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
  public List<Dictionary<string, object>> Convert(DB.Element target)
  {
    List<Dictionary<string, object>> quantities = new();

    if (target.Category.HasMaterialQuantities)
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
          quantities.Add(materialQuantity);
        }
      }
    }

    return quantities;
  }
}

/// <summary>
/// NOTE: Phased out (for now) due to dependency on the MaterialQuantity class, which creates/promotes a bloated data extraction.
/// </summary>
[Obsolete("Creates a rather bloated data structure - 2.0 style. More in the comment above.")]
public class MaterialQuantitiesToSpeckle : ITypedConverter<DB.Element, List<MaterialQuantity>>
{
  private readonly ITypedConverter<DB.Material, RevitMaterial> _revitMaterialConverter;
  private readonly ScalingServiceToSpeckle _scalingService;
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;

  public MaterialQuantitiesToSpeckle(
    ScalingServiceToSpeckle scalingService,
    IConverterSettingsStore<RevitConversionSettings> converterSettings,
    ITypedConverter<DB.Material, RevitMaterial> revitMaterialConverter
  )
  {
    _scalingService = scalingService;
    _converterSettings = converterSettings;
    _revitMaterialConverter = revitMaterialConverter;
  }

  /// <summary>
  /// Material Quantities in Revit are stored in different ways and therefore need to be retrieved
  /// using different methods. According to this forum post https://forums.autodesk.com/t5/revit-api-forum/method-getmaterialarea-appears-to-use-different-formulas-for/td-p/11988215
  /// "Hosts" will return the area of a single side of the object and non-host objects will return the combined area of every side of the element.
  /// Certain MEP element materials are attached to the MEP system that the element belongs to.
  /// POC: We are only sending API-retreivable quantities instead of doing calculations on solids ourselves. Skipping MEP elements for now. Need to validate with users if this fulfills their data extraction workflows.
  /// </summary>
  /// <param name="target"></param>
  /// <returns></returns>
  public List<MaterialQuantity> Convert(DB.Element target)
  {
    // TODO: inefficient layout
    // Creates detached materials
    // Let's create a new class for this with basic props and material name
    // TODO: the above
    List<MaterialQuantity> quantities = new();

    if (target.Category.HasMaterialQuantities)
    {
      foreach (DB.ElementId matId in target.GetMaterialIds(false))
      {
        if (matId is null)
        {
          continue;
        }

        double factor = _scalingService.ScaleLength(1);
        double area = factor * factor * target.GetMaterialArea(matId, false);
        double volume = factor * factor * factor * target.GetMaterialVolume(matId);

        if (_converterSettings.Current.Document.GetElement(matId) is DB.Material material)
        {
          RevitMaterial convertedMaterial = _revitMaterialConverter.Convert(material);
          // NOTE: the RevitMaterial class is semi useless, and it used to extract parameters out too for each material. Overkill.
          quantities.Add(new(convertedMaterial, volume, area, _converterSettings.Current.SpeckleUnits));
        }
      }
    }

    return quantities;
  }
}
