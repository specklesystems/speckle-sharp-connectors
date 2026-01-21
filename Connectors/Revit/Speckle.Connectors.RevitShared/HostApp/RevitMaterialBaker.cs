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

  private ElementId? FindExistingMaterialByName(string? materialName)
  {
    if (string.IsNullOrWhiteSpace(materialName))
    {
      return null;
    }

    string sanitizedName = _revitUtils.RemoveInvalidChars(materialName);

    using var collector = new FilteredElementCollector(_converterSettings.Current.Document);
    var existingMaterial = collector
      .OfClass(typeof(Material))
      .Cast<Material>()
      .FirstOrDefault(m => string.Equals(m.Name, sanitizedName, StringComparison.OrdinalIgnoreCase));

    return existingMaterial?.Id;
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
        // first try to match existing material by name
        ElementId? existingMaterialId = FindExistingMaterialByName(speckleRenderMaterial.name);

        ElementId materialIdToUse;

        if (existingMaterialId != null)
        {
          // Use existing material
          materialIdToUse = existingMaterialId;
        }
        else
        {
          // create new material
          // all values assumed to be on the 0 - 1 scale need to pass through this validation and logging (if assumption wrong)
          double roughness = ClampToUnitRange(speckleRenderMaterial.roughness, "roughness", speckleRenderMaterial.name);
          double opacity = ClampToUnitRange(speckleRenderMaterial.opacity, "opacity", speckleRenderMaterial.name);
          double metalness = ClampToUnitRange(speckleRenderMaterial.metalness, "metalness", speckleRenderMaterial.name);

          var diffuse = System.Drawing.Color.FromArgb(speckleRenderMaterial.diffuse);
          double transparency = 1 - opacity;
          double smoothness = 1 - roughness;
          string materialId = speckleRenderMaterial.applicationId ?? speckleRenderMaterial.id.NotNull();
          string matName = _revitUtils.RemoveInvalidChars(
            $"{speckleRenderMaterial.name}-({materialId})-{baseLayerName}"
          );

          var newMaterialId = Material.Create(_converterSettings.Current.Document, matName);
          var revitMaterial = (Material)_converterSettings.Current.Document.GetElement(newMaterialId);
          revitMaterial.Color = new Color(diffuse.R, diffuse.G, diffuse.B);
          revitMaterial.Transparency = (int)(transparency * 100);
          revitMaterial.Shininess = (int)(metalness * 128);
          revitMaterial.Smoothness = (int)(smoothness * 128);

          materialIdToUse = revitMaterial.Id;
        }

        foreach (var objectId in proxy.objects)
        {
          objectIdAndMaterialIndexMap[objectId] = materialIdToUse;
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

  /// <summary>
  /// After CNX-2661, we've seen some edge cases contradicting the expected 0 - 1 range for PRB properties.
  /// Defensively, we'd rather clamp these values than throw.
  /// </summary>
  /// <remarks>
  /// Created a method so that we can extend the checks to any numerical value potentially leading to a negative value,
  /// which would throw an exception. Generalised method since Math.Clamp() only available since C# 8.0 and this method
  /// handles logging (in the hope that we can get a better feel for these "weird" models, e.g. 0 - 100 scale??)
  /// </remarks>
  private double ClampToUnitRange(double value, string propertyName, string materialName)
  {
    if (value is < 0 or > 1)
    {
      _logger.LogWarning(
        "Material '{MaterialName}' has an invalid {PropertyName} value of {Value} and was clamped to 0 - 1 range",
        materialName,
        propertyName,
        value
      );

      value = Math.Min(Math.Max(0, value), 1);
    }

    return value;
  }
}
