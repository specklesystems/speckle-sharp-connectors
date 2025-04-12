using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Objects.Other;

namespace Speckle.Connectors.GrasshopperShared.Operations.Send;

internal sealed class GrasshopperMaterialPacker
{
  // stores map of render material id to render material proxy
  public Dictionary<string, RenderMaterialProxy> RenderMaterialProxies { get; } = new();

  public GrasshopperMaterialPacker() { }

  /// <summary>
  /// Processes an object or collections's render material and adds the id to a render material proxy in <see cref="RenderMaterialProxies"/>
  /// </summary>
  /// <param name="objId"></param>
  /// <param name="material"></param>
  public void ProcessMaterial(string? objId, SpeckleMaterialWrapper? material)
  {
    if (material is not SpeckleMaterialWrapper matWrapper || objId is not string id)
    {
      return;
    }

    AddObjectIdToMaterialProxy(id, matWrapper);
  }

  private void AddObjectIdToMaterialProxy(string objectId, SpeckleMaterialWrapper matWrapper)
  {
    string matId = matWrapper.applicationId ?? matWrapper.GetSpeckleApplicationId();
    if (RenderMaterialProxies.TryGetValue(matId, out RenderMaterialProxy? proxy))
    {
      proxy.objects.Add(objectId);
    }
    else
    {
      matWrapper.Base.applicationId = matId;
      RenderMaterialProxy newMaterialProxy =
        new()
        {
          value = matWrapper.Base,
          applicationId = matId,
          objects = new()
        };

      newMaterialProxy.objects.Add(objectId);
      RenderMaterialProxies[matId] = newMaterialProxy;
    }
  }
}
