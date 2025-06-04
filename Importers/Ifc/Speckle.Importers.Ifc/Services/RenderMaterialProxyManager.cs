using Speckle.InterfaceGenerator;
using Speckle.Objects.Geometry;
using Speckle.Objects.Other;
using Speckle.Sdk.Common;

namespace Speckle.Importers.Ifc.Services;

[GenerateAutoInterface]
public sealed class RenderMaterialProxyManager : IRenderMaterialProxyManager
{
  public Dictionary<string, RenderMaterialProxy> RenderMaterialProxies { get; } = new();

  public void Clear() => RenderMaterialProxies.Clear();

  public void AddMeshMapping(RenderMaterial renderMaterial, Mesh mesh)
  {
    string materialId = renderMaterial.applicationId.NotNull();
    string meshId = mesh.applicationId.NotNull();

    if (RenderMaterialProxies.TryGetValue(materialId, out RenderMaterialProxy? proxy))
    {
      proxy.objects.Add(meshId);
    }
    else
    {
      RenderMaterialProxies.Add(materialId, new() { objects = [meshId], value = renderMaterial, });
    }
  }
}
