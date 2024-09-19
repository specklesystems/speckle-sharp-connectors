using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Objects.Other.Revit;

namespace Speckle.Converters.RevitShared.ToSpeckle;

public class MaterialAsRevitMaterialConversionToSpeckle : ITypedConverter<DB.Material, RevitMaterial>
{
  private readonly RevitToSpeckleCacheSingleton _revitToSpeckleCacheSingleton;

  public MaterialAsRevitMaterialConversionToSpeckle(RevitToSpeckleCacheSingleton revitToSpeckleCacheSingleton)
  {
    _revitToSpeckleCacheSingleton = revitToSpeckleCacheSingleton;
  }

  public RevitMaterial Convert(DB.Material target)
  {
    RevitMaterial material;
    if (
      _revitToSpeckleCacheSingleton.RevitMaterialCache.TryGetValue(target.UniqueId, out RevitMaterial? cachedMaterial)
    )
    {
      material = cachedMaterial;
    }
    else
    {
      material = ConvertToRevitMaterial(target);
      _revitToSpeckleCacheSingleton.RevitMaterialCache.Add(target.UniqueId, material);
    }

    return material;
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
}
