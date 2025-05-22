using Autodesk.Revit.DB;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Operations.Receive;
using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Objects.Other;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.Extensions;
using Speckle.Sdk.Models.GraphTraversal;

namespace Speckle.Connectors.Revit.HostApp;

/// <summary>
/// Utility class that converts and bakes materials in Revit. Expects to be a scoped dependency per unit of work.
/// </summary>
public class RevitMaterialBaker
{
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private readonly ILogger<RevitMaterialBaker> _logger;
  private readonly RevitUtils _revitUtils;

  public RevitMaterialBaker(
    ILogger<RevitMaterialBaker> logger,
    RevitUtils revitUtils,
    IConverterSettingsStore<RevitConversionSettings> converterSettings
  )
  {
    _logger = logger;
    _revitUtils = revitUtils;
    _converterSettings = converterSettings;
  }

  /// <summary>
  /// Checks the every atomic object has render material or not, if not it tries to find it from its layer tree and mutates
  /// its render material proxy objects list with the traversal current. It will also map displayable objects' display values to their
  /// respective material proxy.
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

      var targetRenderMaterialProxy = unpackedRoot.RenderMaterialProxies.FirstOrDefault(rmp =>
        rmp.objects.Contains(context.Current.applicationId)
      );

      if (targetRenderMaterialProxy is null)
      {
        var layerParents = context.GetAscendants().Where(parent => parent is Layer);

        var layer = layerParents.FirstOrDefault(layer =>
          unpackedRoot.RenderMaterialProxies.Any(rmp => rmp.objects.Contains(layer.applicationId!))
        );

        if (layer is not null)
        {
          var layerRenderMaterialProxy = unpackedRoot.RenderMaterialProxies.First(rmp =>
            rmp.objects.Contains(layer.applicationId!)
          );

          targetRenderMaterialProxy = layerRenderMaterialProxy;
        }
      }

      if (targetRenderMaterialProxy is null)
      {
        continue; // exit fast, no proxy, we can't do much more.
      }
      // We mutate the existing proxy list that comes from source application. Because we do not keep track of parent-child relationship of objects in terms of render materials.
      targetRenderMaterialProxy.objects.Add(context.Current.applicationId!);

      // This is somewhat evil: we're unpacking here displayable elements by adding their display value to the target render material proxy.
      // If the display value items do not have an application id, we will generate one.
      var displayable = context.Current.TryGetDisplayValue();
      if (displayable != null)
      {
        foreach (var @base in displayable)
        {
          if (@base.applicationId == null)
          {
            var guid = Guid.NewGuid().ToString();
            @base.applicationId = guid;
            targetRenderMaterialProxy.objects.Add(guid);
          }
          else
          {
            targetRenderMaterialProxy.objects.Add(@base.applicationId);
          }
        }
      }
    }
  }

  /// <summary>
  /// Will bake render materials in the revit document.
  /// </summary>
  /// <param name="speckleRenderMaterialProxies"></param>
  /// <param name="baseLayerName"></param>
  /// <returns></returns>
  public Dictionary<string, ElementId> BakeMaterials(
    IReadOnlyCollection<RenderMaterialProxy> speckleRenderMaterialProxies,
    string baseLayerName
  )
  {
    Dictionary<string, ElementId> objectIdAndMaterialIndexMap = new();
    foreach (var proxy in speckleRenderMaterialProxies)
    {
      var speckleRenderMaterial = proxy.value;

      try
      {
        var diffuse = System.Drawing.Color.FromArgb(speckleRenderMaterial.diffuse);
        double transparency = 1 - speckleRenderMaterial.opacity;
        double smoothness = 1 - speckleRenderMaterial.roughness;
        string materialId = speckleRenderMaterial.applicationId ?? speckleRenderMaterial.id.NotNull();
        string matName = _revitUtils.RemoveInvalidChars($"{speckleRenderMaterial.name}-({materialId})-{baseLayerName}");

        var newMaterialId = Material.Create(_converterSettings.Current.Document, matName);
        var revitMaterial = (Material)_converterSettings.Current.Document.GetElement(newMaterialId);
        revitMaterial.Color = new Color(diffuse.R, diffuse.G, diffuse.B);
        revitMaterial.Transparency = (int)(transparency * 100);
        revitMaterial.Shininess = (int)(speckleRenderMaterial.metalness * 128);
        revitMaterial.Smoothness = (int)(smoothness * 128);

        foreach (var objectId in proxy.objects)
        {
          objectIdAndMaterialIndexMap[objectId] = revitMaterial.Id;
        }
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogError(ex, "Failed to create material in Revit");
      }
    }

    return objectIdAndMaterialIndexMap;
  }

  public void PurgeMaterials(string baseGroupName)
  {
    var validBaseGroupName = _revitUtils.RemoveInvalidChars(baseGroupName);
    var document = _converterSettings.Current.Document;

    using (var collector = new FilteredElementCollector(document))
    {
      var materialIds = collector
        .OfClass(typeof(Material))
        .Where(m => m.Name.Contains(validBaseGroupName))
        .Select(m => m.Id)
        .ToList();

      document.Delete(materialIds);
    }
  }
}
