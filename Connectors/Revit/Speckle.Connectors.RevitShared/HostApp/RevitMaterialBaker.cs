using Autodesk.Revit.DB;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Utils.Operations.Receive;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Objects.Other;
using Speckle.Sdk;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.GraphTraversal;

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

  /// <summary>
  /// Checks the every atomic object has render material or not, if not it tries to find it from its layer tree and mutates
  /// its render material proxy objects list with the traversal current.
  /// </summary>
  public void MapLayersRenderMaterials(RootObjectUnpackerResult unpackedRoot)
  {
    if (unpackedRoot.RenderMaterialProxies is null)
    {
      return;
    }

    foreach (var context in unpackedRoot.ObjectsToConvert)
    {
      if (context.Current.applicationId is null)
      {
        continue;
      }

      var hasMaterial = unpackedRoot.RenderMaterialProxies.Any(rmp =>
        rmp.objects.Contains(context.Current.applicationId)
      );

      if (!hasMaterial)
      {
        var layerParents = context.GetAscendants().Where(parent => parent is Layer);
        var layer = layerParents.First(layer =>
          unpackedRoot.RenderMaterialProxies.Any(rmp => rmp.objects.Contains(layer.applicationId!))
        );
        if (layer is not null)
        {
          var layerRenderMaterialProxy = unpackedRoot.RenderMaterialProxies.First(rmp =>
            rmp.objects.Contains(layer.applicationId!)
          );
          // We mutate the existing proxy list that comes from source application. Because we do not keep track of parent-child relationship of objects in terms of render materials.
          layerRenderMaterialProxy?.objects.Add(context.Current.applicationId!);
        }
      }
    }
  }

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
