using System.Drawing;
using Speckle.Connector.Tekla2024.Extensions;
using Speckle.Objects.Other;

namespace Speckle.Connector.Tekla2024.HostApp;

public class TeklaMaterialUnpacker
{
  public List<RenderMaterialProxy> UnpackRenderMaterial(List<TSM.ModelObject> atomicObjects)
  {
    Dictionary<string, RenderMaterialProxy> renderMaterialProxies = new();

    var flattenedAtomicObjects = new List<TSM.ModelObject>();

    foreach (var atomicObject in atomicObjects)
    {
      flattenedAtomicObjects.Add(atomicObject);
      foreach (TSM.ModelObject child in atomicObject.GetChildren())
      {
        if (child is TSM.ControlPoint or TSM.Weld or TSM.Fitting)
        {
          continue;
        }
        flattenedAtomicObjects.Add(child);
      }
    }

    foreach (TSM.ModelObject flattenedAtomicObject in flattenedAtomicObjects)
    {
      var color = new TSMUI.Color();
      TSMUI.ModelObjectVisualization.GetRepresentation(flattenedAtomicObject, ref color);
      int r = (int)(color.Red * 255);
      int g = (int)(color.Green * 255);
      int b = (int)(color.Blue * 255);
      int a = (int)(color.Transparency * 255);
      int argb = (a << 24) | (r << 16) | (g << 8) | b;

      Color systemColor = Color.FromArgb(argb);

      var colorId = systemColor.ToArgb().ToString();
      var objectId = flattenedAtomicObject.GetSpeckleApplicationId();
      if (renderMaterialProxies.TryGetValue(colorId, out RenderMaterialProxy? value))
      {
        value.objects.Add(objectId);
      }
      else
      {
        var renderMaterial = new RenderMaterial() { name = colorId, diffuseColor = systemColor };
        RenderMaterialProxy proxyRenderMaterial =
          new()
          {
            value = renderMaterial,
            objects = [objectId],
            applicationId = colorId
          };
        renderMaterialProxies[colorId] = proxyRenderMaterial;
      }
    }

    return renderMaterialProxies.Values.ToList();
  }
}
