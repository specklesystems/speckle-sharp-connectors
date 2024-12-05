using Microsoft.Extensions.Logging;
using Speckle.Converter.Navisworks.Settings;
using Speckle.Converters.Common;
using Speckle.Objects.Other;
using Speckle.Sdk;
using static Speckle.Connector.Navisworks.Extensions.ElementSelectionExtension;

namespace Speckle.Connector.Navisworks.HostApp;

public class NavisworksMaterialUnpacker(
  ILogger<NavisworksMaterialUnpacker> logger,
  IConverterSettingsStore<NavisworksConversionSettings> converterSettings
)
{
  // Helper function to select a property based on the representation mode
  // Selector method for individual properties
  private static T Select<T>(RepresentationMode mode, T active, T permanent, T original, T defaultValue) =>
    mode switch
    {
      RepresentationMode.Active => active,
      RepresentationMode.Permanent => permanent,
      RepresentationMode.Original => original,
      _ => defaultValue,
    };

  public List<RenderMaterialProxy> UnpackRenderMaterial(IReadOnlyList<NAV.ModelItem> navisworksObjects)
  {
    if (navisworksObjects == null)
    {
      throw new ArgumentNullException(nameof(navisworksObjects));
    }

    Dictionary<string, RenderMaterialProxy> renderMaterialProxies = [];

    foreach (NAV.ModelItem navisworksObject in navisworksObjects)
    {
      try
      {
        if (!navisworksObject.HasGeometry)
        {
          continue;
        }

        var navisworksObjectId = ResolveModelItemToIndexPath(navisworksObject);

        var geometry = navisworksObject.Geometry;

        // Extract the current visual representation mode
        var mode = converterSettings.Current.User.VisualRepresentationMode;

        // Assign properties using the selector
        var renderColor = Select(
          mode,
          geometry.ActiveColor,
          geometry.PermanentColor,
          geometry.OriginalColor,
          new NAV.Color(1.0, 1.0, 1.0)
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
          geometry.ActiveColor.GetHashCode(),
          geometry.PermanentColor.GetHashCode(),
          geometry.OriginalColor.GetHashCode(),
          0
        );

        var materialName = $"NavisworksMaterial_{Math.Abs(NavisworksColorToColor(renderColor).ToArgb())}";

        // Alternatively the material could be stored on the Item property
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

        // Or in a Material property
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
          value.objects.Add(navisworksObjectId);
        }
        else
        {
          renderMaterialProxies[renderMaterialId.ToString()] = new RenderMaterialProxy()
          {
            // For now, we will just use the color and transparency to create a new material
            // There is more information that is in the Material object that could be used to create a more accurate material
            // But is constant regardless of the user settings
            value = ConvertRenderColorAndTransparencyToSpeckle(
              materialName,
              renderTransparency,
              renderColor,
              renderMaterialId
            ),
            objects = [navisworksObjectId]
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

  private RenderMaterial ConvertRenderColorAndTransparencyToSpeckle(
    string name,
    double transparency,
    NAV.Color navisworksColor,
    int applicationId
  )
  {
    var color = NavisworksColorToColor(navisworksColor);

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

  private static System.Drawing.Color NavisworksColorToColor(NAV.Color color) =>
    System.Drawing.Color.FromArgb(
      Convert.ToInt32(color.R * 255),
      Convert.ToInt32(color.G * 255),
      Convert.ToInt32(color.B * 255)
    );
}
