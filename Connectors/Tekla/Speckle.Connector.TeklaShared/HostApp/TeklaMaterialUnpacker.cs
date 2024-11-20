using System.Drawing;
using Speckle.Connector.Tekla2024.Extensions;
using Speckle.Objects.Other;

namespace Speckle.Connector.Tekla2024.HostApp;

public class TeklaMaterialUnpacker
{
  public List<RenderMaterialProxy> UnpackRenderMaterial(List<TSM.ModelObject> atomicObjects)
  {
    int counter = 0;
    var renderMaterialProxies = new Dictionary<string, RenderMaterialProxy>();
    var processedObjects = new HashSet<string>();

    foreach (var atomicObject in atomicObjects)
    {
      ProcessModelObject(atomicObject, renderMaterialProxies, processedObjects, ref counter);
    }

    return renderMaterialProxies.Values.ToList();
  }

  // NOTE: Why this function? Previously, if multiple atomicObjects had the same color, the RenderMaterialProxy was overwritten
  private void ProcessModelObject(
    TSM.ModelObject modelObject,
    Dictionary<string, RenderMaterialProxy> renderMaterialProxies,
    HashSet<string> processedObjects,
    ref int duplicateCounter
  )
  {
    // Prevent processing the same object multiple times
    var objectId = modelObject.GetSpeckleApplicationId();
    Type teklaType = modelObject.GetType();
    Console.WriteLine($"Processing model object: {teklaType}");
    if (processedObjects.Contains(objectId))
    {
      duplicateCounter++;
      return;
    }

    processedObjects.Add(objectId);

    var color = new TSMUI.Color();
    TSMUI.ModelObjectVisualization.GetRepresentation(modelObject, ref color);
    int r = (int)(color.Red * 255);
    int g = (int)(color.Green * 255);
    int b = (int)(color.Blue * 255);
    int a = (int)(color.Transparency * 255);
    int argb = (a << 24) | (r << 16) | (g << 8) | b;

    Color systemColor = Color.FromArgb(argb);
    var colorId = color.GetSpeckleApplicationId();

    // Ensure unique RenderMaterialProxy for each color
    if (!renderMaterialProxies.TryGetValue(colorId, out RenderMaterialProxy? renderMaterialProxy))
    {
      renderMaterialProxy = new RenderMaterialProxy
      {
        value = new RenderMaterial { name = colorId, diffuse = systemColor.ToArgb() },
        objects = new List<string>(),
        applicationId = colorId
      };
      renderMaterialProxies[colorId] = renderMaterialProxy;
    }

    renderMaterialProxy.objects.Add(objectId);

    // Recursively process children
    foreach (var child in modelObject.GetSupportedChildren())
    {
      ProcessModelObject(child, renderMaterialProxies, processedObjects, ref duplicateCounter);
    }
  }
}
