using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Services;
using Speckle.Objects.Other;
using Speckle.Objects.Other.Revit;

namespace Speckle.Converters.RevitShared.ToSpeckle;

public class MaterialQuantitiesToSpeckle : ITypedConverter<DB.Element, List<MaterialQuantity>>
{
  private readonly ITypedConverter<DB.Material, (RevitMaterial, RenderMaterial)> _materialConverter;
  private readonly RevitMaterialCacheSingleton _materialCacheSingleton;
  private readonly DisplayValueExtractor _displayValueExtractor;
  private readonly ScalingServiceToSpeckle _scalingService;
  private readonly IRevitConversionContextStack _contextStack;

  public MaterialQuantitiesToSpeckle(
    ITypedConverter<DB.Material, (RevitMaterial, RenderMaterial)> materialConverter,
    RevitMaterialCacheSingleton materialCacheSingleton,
    DisplayValueExtractor displayValueExtractor,
    ScalingServiceToSpeckle scalingService,
    IRevitConversionContextStack contextStack
  )
  {
    _materialConverter = materialConverter;
    _materialCacheSingleton = materialCacheSingleton;
    _displayValueExtractor = displayValueExtractor;
    _scalingService = scalingService;
    _contextStack = contextStack;
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
    List<MaterialQuantity> quantities = new();
    if (target.Category.HasMaterialQuantities)
    {
      foreach (DB.ElementId matId in target.GetMaterialIds(false))
      {
        string? id = matId.ToString();
        if (id is null)
        {
          continue;
        }

        double factor = _scalingService.ScaleLength(1);
        double area = factor * factor * target.GetMaterialArea(matId, false);
        double volume = factor * factor * factor * target.GetMaterialVolume(matId);

        RevitMaterial? revitMaterial = null;
        if (_materialCacheSingleton.ConvertedRevitMaterialMap.TryGetValue(id, out RevitMaterial? cachedMaterial))
        {
          revitMaterial = cachedMaterial;
        }
        else
        {
          if (_contextStack.Current.Document.GetElement(matId) is DB.Material material)
          {
            (RevitMaterial convertedMaterial, RenderMaterial _) = _materialConverter.Convert(material);
            revitMaterial = convertedMaterial;
          }
        }

        if (revitMaterial != null)
        {
          quantities.Add(new(revitMaterial, volume, area, _contextStack.Current.SpeckleUnits));
        }
      }
    }

    return quantities;
  }
}
