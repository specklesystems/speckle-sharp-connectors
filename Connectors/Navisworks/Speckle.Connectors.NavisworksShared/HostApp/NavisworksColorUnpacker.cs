using Microsoft.Extensions.Logging;
using Speckle.Connector.Navisworks.Services;
using Speckle.Converter.Navisworks.Helpers;
using Speckle.Converter.Navisworks.Settings;
using Speckle.Converters.Common;
using Speckle.Sdk;
using Speckle.Sdk.Models.Proxies;
using ComApi = Autodesk.Navisworks.Api.Interop.ComApi;
using ComBridge = Autodesk.Navisworks.Api.ComApi.ComApiBridge;

namespace Speckle.Connector.Navisworks.HostApp;

public class NavisworksColorUnpacker(
  ILogger<NavisworksColorUnpacker> logger,
  IConverterSettingsStore<NavisworksConversionSettings> converterSettings,
  IElementSelectionService selectionService
)
{
  private static T SelectByRepresentationMode<T>(
    RepresentationMode mode, T active, T permanent, T original, T defaultValue) =>
    mode switch
    {
      RepresentationMode.Active => active,
      RepresentationMode.Permanent => permanent,
      RepresentationMode.Original => original,
      _ => defaultValue,
    };

  internal List<ColorProxy> UnpackColor(
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
        if (!Is2DElement(navisworksObject))
        {
          continue;
        }

        var navisworksObjectId = selectionService.GetModelItemPath(navisworksObject);

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
            objects = [finalId]
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

  private static bool Is2DElement(NAV.ModelItem modelItem)
  {
    if (!modelItem.HasGeometry)
    {
      return false;
    }

    var primitiveChecker = new PrimitiveChecker();

    var comSelection = ComBridge.ToInwOpSelection([modelItem]);
    try
    {
      var pathsCollection = comSelection.Paths();
      try
      {
        foreach (ComApi.InwOaPath path in pathsCollection)
        {
          var fragmentsCollection = path.Fragments();
          try
          {
            foreach (ComApi.InwOaFragment3 fragment in fragmentsCollection.OfType<ComApi.InwOaFragment3>())
            {
              fragment.GenerateSimplePrimitives(ComApi.nwEVertexProperty.eNORMAL, primitiveChecker);

              if (primitiveChecker.HasTriangles)
              {
                return false;
              }
            }
          }
          finally
          {
            if (fragmentsCollection != null)
            {
              System.Runtime.InteropServices.Marshal.ReleaseComObject(fragmentsCollection);
            }
          }
        }

        return primitiveChecker.HasLines || primitiveChecker.HasPoints || primitiveChecker.HasSnapPoints;
      }
      finally
      {
        if (pathsCollection != null)
        {
          System.Runtime.InteropServices.Marshal.ReleaseComObject(pathsCollection);
        }
      }
    }
    finally
    {
      if (comSelection != null)
      {
        System.Runtime.InteropServices.Marshal.ReleaseComObject(comSelection);
      }
    }
  }
}

public class PrimitiveChecker : ComApi.InwSimplePrimitivesCB
{
  public bool HasTriangles { get; private set; }
  public bool HasLines { get; private set; }
  public bool HasPoints { get; private set; }
  public bool HasSnapPoints { get; private set; }

  public void Line(ComApi.InwSimpleVertex v1, ComApi.InwSimpleVertex v2) => HasLines = true;

  public void Point(ComApi.InwSimpleVertex v1) => HasPoints = true;

  public void SnapPoint(ComApi.InwSimpleVertex v1) => HasSnapPoints = true;

  public void Triangle(ComApi.InwSimpleVertex v1, ComApi.InwSimpleVertex v2, ComApi.InwSimpleVertex v3) =>
    HasTriangles = true;
}
