using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Objects.Other;
using Speckle.Objects.Other.Revit;

namespace Speckle.Converters.RevitShared.ToSpeckle;

public class MaterialConversionToSpeckle : ITypedConverter<DB.Material, (RevitMaterial, RenderMaterial)>
{
  private readonly RevitMaterialCacheSingleton _materialCacheSingleton;

  public MaterialConversionToSpeckle(RevitMaterialCacheSingleton materialCacheSingleton)
  {
    _materialCacheSingleton = materialCacheSingleton;
  }

  public (RevitMaterial, RenderMaterial) Convert(DB.Material target)
  {
    RevitMaterial material;
    if (
      _materialCacheSingleton.ConvertedRevitMaterialMap.TryGetValue(target.UniqueId, out RevitMaterial? cachedMaterial)
    )
    {
      material = cachedMaterial;
    }
    else
    {
      material = ConvertToRevitMaterial(target);
      _materialCacheSingleton.ConvertedRevitMaterialMap.Add(target.UniqueId, material);
    }

    RenderMaterial renderMaterial;
    if (
      _materialCacheSingleton.ConvertedRenderMaterialMap.TryGetValue(
        target.UniqueId,
        out RenderMaterial? cachedRenderMaterial
      )
    )
    {
      renderMaterial = cachedRenderMaterial;
    }
    else
    {
      renderMaterial = ConvertToRenderMaterial(target);
      _materialCacheSingleton.ConvertedRenderMaterialMap.Add(target.UniqueId, renderMaterial);
    }

    return (material, renderMaterial);
  }

  private RevitMaterial ConvertToRevitMaterial(DB.Material target)
  {
    // POC: we need to validate these properties on the RevitMaterial class, ie are they used?
    RevitMaterial material =
      new(
        target.Name,
        target.MaterialCategory,
        target.MaterialClass,
        target.Shininess,
        target.Smoothness,
        target.Transparency
      )
      {
        applicationId = target.UniqueId
      };

    // POC: I'm removing material parameter assigning here as we're exploding a bit the whole space with too many parameters.
    // We can bring this back if needed/requested by our end users.
    // _parameterObjectAssigner.AssignParametersToBase(target, material);
    return material;
  }

  private RenderMaterial ConvertToRenderMaterial(DB.Material target)
  {
    RenderMaterial renderMaterial =
      new()
      {
        name = target.Name,
        opacity = 1 - target.Transparency / 100d,
        diffuse = System.Drawing.Color.FromArgb(target.Color.Red, target.Color.Green, target.Color.Blue).ToArgb(),
        applicationId = target.UniqueId
      };

    return renderMaterial;
  }
}
