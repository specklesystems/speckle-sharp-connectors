using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Objects.Other;

namespace Speckle.Converters.RevitShared.ToSpeckle;

public class MaterialAsSpeckleMaterialConversionToSpeckle : ITypedConverter<DB.Material, RenderMaterial>
{
  private readonly RevitToSpeckleCacheSingleton _revitToSpeckleCacheSingleton;

  public MaterialAsSpeckleMaterialConversionToSpeckle(RevitToSpeckleCacheSingleton revitToSpeckleCacheSingleton)
  {
    _revitToSpeckleCacheSingleton = revitToSpeckleCacheSingleton;
  }

  public RenderMaterial Convert(DB.Material target)
  {
    RenderMaterial renderMaterial;
    if (
      _revitToSpeckleCacheSingleton.SpeckleRenderMaterialCache.TryGetValue(
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
      _revitToSpeckleCacheSingleton.SpeckleRenderMaterialCache.Add(target.UniqueId, renderMaterial);
    }

    return renderMaterial;
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
