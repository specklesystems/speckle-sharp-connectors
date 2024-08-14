using Speckle.Objects;
using Speckle.Objects.Geometry;
using Speckle.Objects.Other;
using Speckle.Sdk.Models;

namespace Speckle.Connectors.Revit.HostApp;

public class SendMaterialManager
{
  public Dictionary<string, RenderMaterialProxy> RenderMaterialProxies { get; } = new();

  public void AddObjectToRenderMaterialMap(Base obj)
  {
    if (obj is not IDisplayValue<List<Mesh>> displayable)
    {
      return;
    }

    foreach (var mesh in displayable.displayValue)
    {
      var renderMaterial = mesh["renderMaterial"] as RenderMaterial;
      if (renderMaterial == null)
      {
        continue;
      }

      var renderMaterialId = renderMaterial.applicationId ?? renderMaterial.GetId();
      var objectId = mesh.applicationId ?? mesh.GetId();

      if (!RenderMaterialProxies.TryGetValue(renderMaterialId, out RenderMaterialProxy? proxy))
      {
        RenderMaterialProxies[renderMaterialId] = new RenderMaterialProxy()
        {
          applicationId = renderMaterialId,
          value = renderMaterial,
          objects = [objectId]
        };
        continue;
      }
      proxy.objects.Add(objectId);
    }
  }
}
