using Autodesk.AutoCAD.Colors;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Operations;
using Speckle.Sdk;
using Speckle.Sdk.Models.Proxies;
using AutocadColor = Autodesk.AutoCAD.Colors.Color;

namespace Speckle.Connectors.Autocad.HostApp;

/// <summary>
/// Expects to be a scoped dependency for a given operation and helps with layer creation and cleanup.
/// </summary>
public class AutocadColorBaker
{
  private readonly ILogger<AutocadColorBaker> _logger;

  public AutocadColorBaker(ILogger<AutocadColorBaker> logger)
  {
    _logger = logger;
  }

  /// <summary>
  /// For receive operations
  /// </summary>
  public Dictionary<string, AutocadColor> ObjectColorsIdMap { get; } = new();

  /// <summary>
  /// Parse Color Proxies and stores in ObjectColorIdMap the relationship between object ids and colors
  /// </summary>
  /// <param name="colorProxies"></param>
  /// <param name="onOperationProgressed"></param>
  public async Task ParseColors(IReadOnlyCollection<ColorProxy> colorProxies, IProgress<ProgressAction> onOperationProgressed)
  {
    var count = 0;
    foreach (ColorProxy colorProxy in colorProxies)
    {
      try
      {
        onOperationProgressed
          .Report(new("Converting colors", (double)++count / colorProxies.Count));

        // skip any colors with source = layer, since object color default source is by layer
        if (colorProxy["source"] is string source && source == "layer")
        {
          continue;
        }

        foreach (string objectId in colorProxy.objects)
        {
          AutocadColor convertedColor = ConvertColorProxyToColor(colorProxy);
#if NET8_0
          ObjectColorsIdMap.TryAdd(objectId, convertedColor);
#else
          if (!ObjectColorsIdMap.ContainsKey(objectId))
          {
            ObjectColorsIdMap.Add(objectId, convertedColor);
          }
#endif
        }
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogError(ex, "Failed parsing color proxy");
      }

      await Task.Yield();
    }
  }

  private AutocadColor ConvertColorProxyToColor(ColorProxy colorProxy)
  {
    // if source = block, return a default ByBlock color
    if (colorProxy["source"] is string source && source == "block")
    {
      return AutocadColor.FromColorIndex(ColorMethod.ByBlock, 0);
    }

    return colorProxy["autocadColorIndex"] is long index
      ? AutocadColor.FromColorIndex(ColorMethod.ByAci, (short)index)
      : AutocadColor.FromColor(System.Drawing.Color.FromArgb(colorProxy.value));
  }
}
