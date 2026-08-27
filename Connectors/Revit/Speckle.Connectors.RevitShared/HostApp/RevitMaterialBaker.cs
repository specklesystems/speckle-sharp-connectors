using Autodesk.Revit.DB;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Operations.Receive;
using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Objects.Other;
using Speckle.Sdk;
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

  private readonly List<string> _createdMaterialUniqueIds = new();

  /// <summary>UniqueIds of the Materials this receive <b>created</b> in the project document — recorded into the
  /// receive manifest so the next receive can delete exactly these [ENG-8805]. Materials that were reused (an existing
  /// document material matched by name) are deliberately absent: they are the user's, not ours to delete. Materials
  /// created inside a family document are absent too — their UniqueIds are meaningless in the project document.</summary>
  public IReadOnlyCollection<string> CreatedMaterialUniqueIds => _createdMaterialUniqueIds;

  private ElementId? FindExistingMaterialByName(string? materialName, Document document)
  {
    if (string.IsNullOrWhiteSpace(materialName))
    {
      return null;
    }

    string sanitizedName = _revitUtils.RemoveInvalidChars(materialName!);

    using var collector = new FilteredElementCollector(document);
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
  /// Bakes a single Speckle RenderMaterial into the provided document.
  /// Used both for project-level baking and isolated family-level baking.
  /// </summary>
  public ElementId BakeMaterial(RenderMaterial speckleRenderMaterial, Document document)
  {
    ElementId? existingMaterialId = FindExistingMaterialByName(speckleRenderMaterial.name, document);

    if (existingMaterialId != null)
    {
      return existingMaterialId;
    }

    // create new material
    // all values assumed to be on the 0 - 1 scale need to pass through this validation and logging (if assumption wrong)
    double roughness = ClampToUnitRange(speckleRenderMaterial.roughness, "roughness", speckleRenderMaterial.name);
    double opacity = ClampToUnitRange(speckleRenderMaterial.opacity, "opacity", speckleRenderMaterial.name);
    double metalness = ClampToUnitRange(speckleRenderMaterial.metalness, "metalness", speckleRenderMaterial.name);

    var diffuse = System.Drawing.Color.FromArgb(speckleRenderMaterial.diffuse);
    double transparency = 1 - opacity;
    double smoothness = 1 - roughness;
    string matName = _revitUtils.RemoveInvalidChars($"{speckleRenderMaterial.name}");

    var newMaterialId = Material.Create(document, matName);
    var revitMaterial = (Material)document.GetElement(newMaterialId);
    revitMaterial.Color = new Color(diffuse.R, diffuse.G, diffuse.B);
    revitMaterial.Transparency = (int)(transparency * 100);
    revitMaterial.Shininess = (int)(metalness * 128);
    revitMaterial.Smoothness = (int)(smoothness * 100);

    // Only project-document materials are trackable: a family document's UniqueIds don't resolve in the project.
    if (document.Equals(_converterSettings.Current.Document))
    {
      _createdMaterialUniqueIds.Add(revitMaterial.UniqueId);
    }

    return revitMaterial.Id;
  }

  /// <summary>
  /// Will bake render materials in the project document.
  /// </summary>
  public Dictionary<string, ElementId> BakeMaterials(
    IReadOnlyCollection<RenderMaterialProxy> speckleRenderMaterialProxies
  )
  {
    Dictionary<string, ElementId> objectIdAndMaterialIndexMap = new();
    var document = _converterSettings.Current.Document;

    foreach (var proxy in speckleRenderMaterialProxies)
    {
      try
      {
        ElementId materialIdToUse = BakeMaterial(proxy.value, document);

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

  /// <summary>
  /// Deletes the Materials a previous receive of this model created, identified by the UniqueIds it recorded in the
  /// receive manifest (<see cref="RevitReceiveManifest"/>).
  /// </summary>
  /// <remarks>
  /// <para>Replaces a name-based purge that deleted every Material whose name <i>contained</i> the base group name.
  /// That predicate never matched anything this baker creates — <see cref="BakeMaterial"/> names materials after the
  /// Speckle material alone, with no project/model suffix — so it was a no-op that nonetheless carried an unscoped
  /// delete: a user's own material named after the project or model would have been removed [ENG-8805].</para>
  /// <para>Call this only after the prior bake's elements are gone, so the materials being deleted are no longer
  /// painted onto anything this receive is about to replace.</para>
  /// </remarks>
  public void PurgeMaterials(IReadOnlyCollection<string> materialUniqueIds)
  {
    if (materialUniqueIds.Count == 0)
    {
      return;
    }

    var document = _converterSettings.Current.Document;
    var toDelete = new List<ElementId>();
    foreach (var uniqueId in materialUniqueIds)
    {
      // Anything but a Material means the id was recycled by another element — never delete on a stale match.
      if (document.GetElement(uniqueId) is Material material)
      {
        toDelete.Add(material.Id);
      }
    }

    if (toDelete.Count > 0)
    {
      document.Delete(toDelete);
    }
  }

  /// <summary>
  /// After CNX-2661, we've seen some edge cases contradicting the expected 0 - 1 range for PRB properties.
  /// Defensively, we'd rather clamp these values than throw.
  /// </summary>
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
