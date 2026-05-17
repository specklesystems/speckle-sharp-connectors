using Microsoft.Extensions.Logging;
using Speckle.Converter.Navisworks.Helpers;
using Speckle.Converter.Navisworks.Services;
using Speckle.Converter.Navisworks.Settings;
using Speckle.Converters.Common;
using Speckle.Sdk;
using Speckle.Sdk.Models.Proxies;

namespace Speckle.Connector.Navisworks.HostApp;

public class NavisworksColorUnpacker(
  ILogger<NavisworksColorUnpacker> logger,
  IConverterSettingsStore<NavisworksConversionSettings> converterSettings,
  IElementSelectionService selectionService
)
{
  private static T SelectByRepresentationMode<T>(
    RepresentationMode mode,
    T active,
    T permanent,
    T original,
    T defaultValue
  ) =>
    mode switch
    {
      RepresentationMode.Active => active,
      RepresentationMode.Permanent => permanent,
      RepresentationMode.Original => original,
      _ => defaultValue,
    };

  internal List<ColorProxy> UnpackColor(
    IReadOnlyList<NAV.ModelItem> navisworksObjects,
    Dictionary<string, List<NAV.ModelItem>> groupedNodes,
    ISet<string> twoDElementPaths
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
    if (twoDElementPaths == null)
    {
      throw new ArgumentNullException(nameof(twoDElementPaths));
    }

    Dictionary<string, ColorProxy> colorProxies = [];
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
        if (!twoDElementPaths.Contains(navisworksObjectId))
        {
          continue;
        }

        var finalId = mergedIds.TryGetValue(navisworksObjectId, out var mergedId) ? mergedId : navisworksObjectId;

        var geometry = navisworksObject.Geometry;
        var mode = converterSettings.Current.User.VisualRepresentationMode;

        using var defaultColor = new NAV.Color(1.0, 1.0, 1.0);

        var representationColor = SelectByRepresentationMode(
          mode,
          geometry.ActiveColor,
          geometry.PermanentColor,
          geometry.OriginalColor,
          defaultColor
        );
        var colorId = SelectByRepresentationMode(
          mode,
          $"{geometry.ActiveColor.GetHashCode()}_{geometry.ActiveTransparency}".GetHashCode(),
          $"{geometry.PermanentColor.GetHashCode()}_{geometry.PermanentTransparency}".GetHashCode(),
          $"{geometry.OriginalColor.GetHashCode()}_{geometry.OriginalTransparency}".GetHashCode(),
          0
        );

        var colorName = ColorConverter.NavisworksColorToColor(representationColor).Name;

        if (colorProxies.TryGetValue(colorId.ToString(), out ColorProxy? colorProxy))
        {
          colorProxy.objects.Add(finalId);
        }
        else
        {
          colorProxies[colorId.ToString()] = new ColorProxy
          {
            value = ColorConverter.NavisworksColorToColor(representationColor).ToArgb(),
            name = colorName,
            applicationId = colorId.ToString(),
            objects = [finalId],
          };
        }
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        logger.LogError(ex, "Failed to unpack color for Navisworks object");
      }
    }

    return colorProxies.Values.ToList();
  }
}
