using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Objects.Other.Revit;

namespace Speckle.Converters.RevitShared.ToSpeckle;

public class MaterialConversionToSpeckleRevitMaterial : ITypedConverter<DB.Material, RevitMaterial>
{
  private readonly ParameterObjectAssigner _parameterObjectAssigner;
  private readonly RevitMaterialCacheSingleton _materialCacheSingleton;

  public MaterialConversionToSpeckleRevitMaterial(
    ParameterObjectAssigner parameterObjectAssigner,
    RevitMaterialCacheSingleton materialCacheSingleton
  )
  {
    _parameterObjectAssigner = parameterObjectAssigner;
    _materialCacheSingleton = materialCacheSingleton;
  }

  public RevitMaterial Convert(DB.Material target)
  {
    if (_materialCacheSingleton.ConvertedRevitMaterialMap.TryGetValue(target.UniqueId, out RevitMaterial? material))
    {
      return material;
    }

    RevitMaterial newMaterial =
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

    _parameterObjectAssigner.AssignParametersToBase(target, newMaterial);

#if NET8_0
    _materialCacheSingleton.ConvertedRevitMaterialMap.TryAdd(target.UniqueId, newMaterial);
#else
    if (!_materialCacheSingleton.ConvertedRevitMaterialMap.ContainsKey(target.UniqueId))
    {
      _materialCacheSingleton.ConvertedRevitMaterialMap.Add(target.UniqueId, newMaterial);
    }
#endif

    return newMaterial;
  }
}
