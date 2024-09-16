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

  public RevitMaterialBaker(IRevitConversionContextStack contextStack, ILogger<RevitMaterialBaker> logger)
  {
    _contextStack = contextStack;
    _logger = logger;
  }

  public void BakeMaterials(List<RenderMaterialProxy> speckleRenderMaterialProxies)
  {
    foreach (var proxy in speckleRenderMaterialProxies)
    {
      var speckleRenderMaterial = proxy.value;

      try
      {
        string materialId = speckleRenderMaterial.applicationId ?? speckleRenderMaterial.id;
        string matName = $"{speckleRenderMaterial.name}-({materialId})";
        //string matName = $"{speckleRenderMaterial.name}-({materialId})-{baseLayerName}";
        //matName = matName.Replace("[", "").Replace("]", ""); // "Material" doesn't like square brackets if we create from here. Once they created from Rhino UI, all good..
        var diffuse = System.Drawing.Color.FromArgb(speckleRenderMaterial.diffuse);
        var emissive = System.Drawing.Color.FromArgb(speckleRenderMaterial.emissive);
        double transparency = 1 - speckleRenderMaterial.opacity;

        var newMaterialId = Autodesk.Revit.DB.Material.Create(_contextStack.Current.Document, "MyNewMaterial");
        var revitMaterial = (Autodesk.Revit.DB.Material)_contextStack.Current.Document.GetElement(newMaterialId);
        revitMaterial.Color = new Color(diffuse.R, diffuse.G, diffuse.B);
        revitMaterial.Transparency = (int)(transparency * 100);
        revitMaterial.Shininess = 75;
        revitMaterial.Smoothness = 25;
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogError(ex, "Failed to create material in Revit");
      }
    }
  }
}
