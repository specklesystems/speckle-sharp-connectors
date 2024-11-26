using System.Drawing;
using Speckle.Connector.Tekla2024.Extensions;
using Speckle.Objects.Other;

namespace Speckle.Connector.Tekla2024.HostApp;

public class TeklaMaterialUnpacker
{
  private readonly TeklaMaterialCache _materialCache;

  public TeklaMaterialUnpacker(TeklaMaterialCache materialCache)
  {
    _materialCache = materialCache;
  }

  public List<RenderMaterialProxy> UnpackRenderMaterial(List<TSM.ModelObject> atomicObjects)
  {
    Dictionary<string, RenderMaterialProxy> renderMaterialProxies = new();

    var flattenedAtomicObjects = new List<TSM.ModelObject>();

    // flatten objects and their children
    foreach (var atomicObject in atomicObjects)
    {
      flattenedAtomicObjects.Add(atomicObject);
      flattenedAtomicObjects.AddRange(atomicObject.GetSupportedChildren().ToList());
    }

    // process each object
    foreach (TSM.ModelObject obj in flattenedAtomicObjects)
    {
      var color = new TSMUI.Color();
      TSMUI.ModelObjectVisualization.GetRepresentation(obj, ref color);

      // create ARGB value consistently
      int r = (int)(color.Red * 255);
      int g = (int)(color.Green * 255);
      int b = (int)(color.Blue * 255);
      int a = (int)(color.Transparency * 255);
      int argb = (a << 24) | (r << 16) | (g << 8) | b;

      // create consistent color ID
      string colorId = color.GetSpeckleApplicationId();
      string objectId = obj.GetSpeckleApplicationId();

      // get or create proxy
      if (!renderMaterialProxies.TryGetValue(colorId, out RenderMaterialProxy? proxy))
      {
        RenderMaterial renderMaterial;
        if (_materialCache.MaterialCache.TryGetValue(colorId, out RenderMaterial? cachedMaterial))
        {
          renderMaterial = cachedMaterial;
        }
        else
        {
          var systemColor = Color.FromArgb(argb);
          renderMaterial = new RenderMaterial
          {
            name = $"Color_{colorId}",
            diffuse = systemColor.ToArgb(),
            opacity = 1,
            applicationId = colorId
          };
          _materialCache.MaterialCache[colorId] = renderMaterial;
        }

        proxy = new RenderMaterialProxy
        {
          value = renderMaterial,
          objects = new List<string>(),
          applicationId = colorId
        };
        renderMaterialProxies[colorId] = proxy;
      }

      proxy.objects.Add(objectId);

      // update object -> proxy mapping
      if (!_materialCache.ObjectProxyMap.TryGetValue(objectId, out var proxyMap))
      {
        proxyMap = new Dictionary<string, RenderMaterialProxy>();
        _materialCache.ObjectProxyMap[objectId] = proxyMap;
      }
      proxyMap[colorId] = proxy;
    }

    return renderMaterialProxies.Values.ToList();
  }
}
