using System.Drawing;
using Microsoft.Extensions.Logging;
using Speckle.Connector.Tekla2024.Extensions;
using Speckle.Objects.Other;

namespace Speckle.Connector.Tekla2024.HostApp;

public class TeklaMaterialUnpacker
{
  private readonly ILogger<TeklaMaterialUnpacker> _logger;

  public TeklaMaterialUnpacker(ILogger<TeklaMaterialUnpacker> logger)
  {
    _logger = logger;
  }

  public List<RenderMaterialProxy> UnpackRenderMaterial(List<TSM.ModelObject> atomicObjects)
  {
    var renderMaterialProxies = new Dictionary<string, RenderMaterialProxy>();
    var processedObjects = new HashSet<string>();

    foreach (var atomicObject in atomicObjects)
    {
      ProcessModelObject(atomicObject, renderMaterialProxies, processedObjects);
    }

    return renderMaterialProxies.Values.ToList();
  }

  private void ProcessModelObject(
    TSM.ModelObject modelObject,
    Dictionary<string, RenderMaterialProxy> renderMaterialProxies,
    HashSet<string> processedObjects
  )
  {
    var objectId = modelObject.GetSpeckleApplicationId();

    // NOTE: Related to CNX 798, processing of BooleanPart led to renderMaterial overwrites. Hence, it was excluded
    // If duplicate objectIds are still appearing, there is another type causing issues.
    if (processedObjects.Contains(objectId))
    {
      _logger.LogError(
        $"The objectId {objectId} had already been processed. Check ModelObjectExtension.cs for nested object circular references."
      );
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

    // Recursively process children (not included in s_excludedTypes)
    foreach (var child in modelObject.GetSupportedChildren())
    {
      ProcessModelObject(child, renderMaterialProxies, processedObjects);
    }
  }
}
