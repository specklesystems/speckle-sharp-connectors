using Speckle.Connectors.Common.Operations.Receive;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Objects.Other;
using Speckle.Sdk;

namespace Speckle.Connectors.GrasshopperShared.Operations.Receive;

internal sealed class GrasshopperMaterialUnpacker
{
  // stores map of object id to material wrapper
  public Dictionary<string, SpeckleMaterialWrapper> Cache { get; private set; } = new();

  // stores converted speckle material wrappers
  private Dictionary<string, SpeckleMaterialWrapper> ConvertedCache { get; set; } = new();

  public GrasshopperMaterialUnpacker(RootObjectUnpackerResult root)
  {
    if (root.RenderMaterialProxies != null)
    {
      ParseMaterials(root.RenderMaterialProxies);
    }
  }

  /// <summary>
  /// Parse Render Material Proxies and stores in Cache the relationship between object ids and materials
  /// </summary>
  /// <param name="materialProxies"></param>
  private void ParseMaterials(IReadOnlyCollection<RenderMaterialProxy> materialProxies)
  {
    foreach (RenderMaterialProxy materialProxy in materialProxies)
    {
      try
      {
        // get the material wrapper for the render material proxy
        string materialId = materialProxy.value.applicationId ?? materialProxy.value.id ?? Guid.NewGuid().ToString();
        if (ConvertedCache.TryGetValue(materialId, out SpeckleMaterialWrapper? materialWrapper))
        {
          ConvertedCache.Add(materialId, materialWrapper);
        }
        else
        {
          // convert the render material to a material wrapper
          SpeckleMaterialWrapperGoo wrapperGoo = new();
          wrapperGoo.CastFrom(materialProxy.value);
          materialWrapper = wrapperGoo.Value;
          materialWrapper.ApplicationId = materialId; // update the id here in case it was mutated
          ConvertedCache.Add(materialId, materialWrapper);
        }

        // assign material wrapper to objects
        foreach (string objectId in materialProxy.objects)
        {
          if (!Cache.TryGetValue(objectId, out SpeckleMaterialWrapper? _))
          {
            Cache.Add(objectId, materialWrapper);
          }
        }
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        // TODO: add error
      }
    }
  }
}
