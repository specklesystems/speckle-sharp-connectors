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

      Color systemColor = Color.FromArgb((int)color.Transparency, (int)color.Red, (int)color.Green, (int)color.Blue);

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
