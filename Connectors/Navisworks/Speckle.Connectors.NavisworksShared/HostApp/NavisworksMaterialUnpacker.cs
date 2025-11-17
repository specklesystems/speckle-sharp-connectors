using Autodesk.Navisworks.Api.ComApi;
using Autodesk.Navisworks.Api.Interop.ComApi;
using Microsoft.Extensions.Logging;
using Speckle.Connector.Navisworks.Services;
using Speckle.Converter.Navisworks.Helpers;
using Speckle.Converter.Navisworks.Settings;
using Speckle.Converter.Navisworks.ToSpeckle;
using Speckle.Converters.Common;
using Speckle.Objects.Other;
using Speckle.Sdk;

namespace Speckle.Connector.Navisworks.HostApp;

public class NavisworksMaterialUnpacker(
  ILogger<NavisworksMaterialUnpacker> logger,
  IConverterSettingsStore<NavisworksConversionSettings> converterSettings,
  IElementSelectionService selectionService,
  GeometryToSpeckleConverter converter
)
{
  private static T Select<T>(RepresentationMode mode, T active, T permanent, T original, T defaultValue) =>
    mode switch
    {
      RepresentationMode.Active => active,
      RepresentationMode.Permanent => permanent,
      RepresentationMode.Original => original,
      _ => defaultValue,
    };

  internal List<RenderMaterialProxy> UnpackRenderMaterial(
    IReadOnlyList<NAV.ModelItem> navisworksObjects,
    Dictionary<string, List<NAV.ModelItem>> groupedNodes
  )
  {
    if (navisworksObjects == null)
    {
      throw new ArgumentNullException(nameof(navisworksObjects));
    }

    if (groupedNodes == null)
    {
      throw new ArgumentNullException(nameof(groupedNodes));
    }

    Dictionary<string, RenderMaterialProxy> renderMaterialProxies = [];
    Dictionary<string, string> mergedIds = [];

    foreach (var group in groupedNodes)
    {
      string groupKey = group.Key;

      foreach (var nodePath in group.Value.Select(selectionService.GetModelItemPath))
      {
        mergedIds[nodePath] = groupKey;
      }
    }

    foreach (NAV.ModelItem navisworksObject in navisworksObjects)
    {
      try
      {
        if (!navisworksObject.HasGeometry)
        {
          continue;
        }

        var navisworksObjectId = selectionService.GetModelItemPath(navisworksObject);
        var finalId = mergedIds.TryGetValue(navisworksObjectId, out var mergedId) ? mergedId : navisworksObjectId;

        var item = selectionService.GetModelItemFromPath(finalId);
        string hashId = "";
        var comSelection = ComApiBridge.ToInwOpSelection([item]);
        var paths = comSelection.Paths();
        var path = paths.OfType<InwOaPath>().First();
        var fragments = path.Fragments();
        if (fragments.Count > 1)
        {
          var fragmentId = converter.GenerateFragmentId(paths);
          hashId = $"geom_{fragmentId}";
        }

        var geometry = navisworksObject.Geometry;
        var mode = converterSettings.Current.User.VisualRepresentationMode;

        using var defaultColor = new NAV.Color(1.0, 1.0, 1.0);

        var renderColor = Select(
          mode,
          geometry.ActiveColor,
          geometry.PermanentColor,
          geometry.OriginalColor,
          defaultColor
        );
        var renderTransparency = Select(
          mode,
          geometry.ActiveTransparency,
          geometry.PermanentTransparency,
          geometry.OriginalTransparency,
          0.0
        );
        var renderMaterialId = Select(
          mode,
          $"{geometry.ActiveColor.GetHashCode()}_{geometry.ActiveTransparency}".GetHashCode(),
          $"{geometry.PermanentColor.GetHashCode()}_{geometry.PermanentTransparency}".GetHashCode(),
          $"{geometry.OriginalColor.GetHashCode()}_{geometry.OriginalTransparency}".GetHashCode(),
          0
        );

        var materialName =
          $"NavisworksMaterial_{Math.Abs(ColorConverter.NavisworksColorToColor(renderColor).ToArgb())}";

        var itemCategory = navisworksObject.PropertyCategories.FindCategoryByDisplayName("Item");
        if (itemCategory != null)
        {
          var itemProperties = itemCategory.Properties;
          var itemMaterial = itemProperties.FindPropertyByDisplayName("Material");
          if (itemMaterial != null && !string.IsNullOrEmpty(itemMaterial.DisplayName))
          {
            materialName = itemMaterial.Value.ToDisplayString();
          }
        }

        var materialPropertyCategory = navisworksObject.PropertyCategories.FindCategoryByDisplayName("Material");
        if (materialPropertyCategory != null)
        {
          var material = materialPropertyCategory.Properties;
          var name = material.FindPropertyByDisplayName("Name");
          if (name != null && !string.IsNullOrEmpty(name.DisplayName))
          {
            materialName = name.Value.ToDisplayString();
          }
        }

        if (renderMaterialProxies.TryGetValue(renderMaterialId.ToString(), out RenderMaterialProxy? value))
        {
          value.objects.Add(!string.IsNullOrEmpty(hashId) ? hashId : finalId);
        }
        else
        {
          renderMaterialProxies[renderMaterialId.ToString()] = new RenderMaterialProxy()
          {
            value = ConvertRenderColorAndTransparencyToSpeckle(
              materialName,
              renderTransparency,
              renderColor,
              renderMaterialId
            ),
            objects = [!string.IsNullOrEmpty(hashId) ? hashId : finalId]
          };
        }
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        logger.LogError(ex, "Failed to unpack render material from Navisworks object");
      }
    }

    return renderMaterialProxies.Values.ToList();
  }

  private static RenderMaterial ConvertRenderColorAndTransparencyToSpeckle(
    string name,
    double transparency,
    NAV.Color navisworksColor,
    int applicationId
  )
  {
    var color = ColorConverter.NavisworksColorToColor(navisworksColor);

    var speckleRenderMaterial = new RenderMaterial()
    {
      name = !string.IsNullOrEmpty(name) ? name : $"NavisworksMaterial_{Math.Abs(color.ToArgb())}",
      opacity = 1 - transparency,
      metalness = 0,
      roughness = 1,
      diffuse = color.ToArgb(),
      emissive = 0,
      applicationId = applicationId.ToString()
    };

    return speckleRenderMaterial;
  }
}
