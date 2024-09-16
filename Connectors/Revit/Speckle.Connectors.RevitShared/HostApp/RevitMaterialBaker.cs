using Autodesk.Revit.DB;
using Microsoft.Extensions.Logging;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Objects.Other;
using Speckle.Sdk;

namespace Speckle.Connectors.Revit.HostApp;

public class RevitMaterialBaker
{
  private readonly IRevitConversionContextStack _contextStack;
  private readonly ILogger<RevitMaterialBaker> _logger;
  private readonly RevitUtils _revitUtils;

  public RevitMaterialBaker(
    IRevitConversionContextStack contextStack,
    ILogger<RevitMaterialBaker> logger,
    RevitUtils revitUtils
  )
  {
    _contextStack = contextStack;
    _logger = logger;
    _revitUtils = revitUtils;
  }

  public Dictionary<string, string> ObjectIdAndMaterialIndexMap { get; } = new();

  public void BakeMaterials(List<RenderMaterialProxy> speckleRenderMaterialProxies, string baseLayerName)
  {
    foreach (var proxy in speckleRenderMaterialProxies)
    {
      var speckleRenderMaterial = proxy.value;

      try
      {
        var diffuse = System.Drawing.Color.FromArgb(speckleRenderMaterial.diffuse);
        double transparency = 1 - speckleRenderMaterial.opacity;
        double smoothness = 1 - speckleRenderMaterial.roughness;
        string materialId = speckleRenderMaterial.applicationId ?? speckleRenderMaterial.id;
        string matName = _revitUtils.RemoveInvalidChars($"{speckleRenderMaterial.name}-({materialId})-{baseLayerName}");

        var newMaterialId = Autodesk.Revit.DB.Material.Create(_contextStack.Current.Document, matName);
        var revitMaterial = (Autodesk.Revit.DB.Material)_contextStack.Current.Document.GetElement(newMaterialId);
        revitMaterial.Color = new Color(diffuse.R, diffuse.G, diffuse.B);

        revitMaterial.Transparency = (int)(transparency * 100);
        revitMaterial.Shininess = (int)(speckleRenderMaterial.metalness * 128);
        revitMaterial.Smoothness = (int)(smoothness * 128);

        foreach (var objectId in proxy.objects)
        {
          ObjectIdAndMaterialIndexMap[objectId] = revitMaterial.Id.ToString();
        }
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogError(ex, "Failed to create material in Revit");
      }
    }
  }
}
