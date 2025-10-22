using Microsoft.Extensions.Logging;
using Rhino.DocObjects;
using Rhino.DocObjects.Tables;
using Speckle.Converters.Common;
using Speckle.Objects.Other;
using Speckle.Sdk;

namespace Speckle.Connectors.Rhino.HostApp;

public class RhinoViewUnpacker
{
  private readonly IRootToSpeckleConverter _rootToSpeckleConverter;
  private readonly ILogger<RhinoViewUnpacker> _logger;

  public RhinoViewUnpacker(IRootToSpeckleConverter rootToSpeckleConverter, ILogger<RhinoViewUnpacker> logger)
  {
    _rootToSpeckleConverter = rootToSpeckleConverter;
    _logger = logger;
  }

  /// <summary>
  /// For send operations
  /// </summary>
  private Dictionary<string, ViewProxy> ViewProxies { get; } = new();

  private ViewProxy? ConvertViewToViewProxy(ViewInfo? view)
  {
    if (view is null)
    {
      return null;
    }

    try
    {
      var converted = (Speckle.Objects.Other.Camera)_rootToSpeckleConverter.Convert(view);
      if (converted != null)
      {
        return new()
        {
          name = view.Name,
          value = converted,
          applicationId = view.Name,
          objects = new()
        };
      }
      else
      {
        return null;
      }
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, "Failed to create a view proxy from {view}", view.Name);
      return null;
    }
  }

  /// <summary>
  /// Iterates through a given set of rhino named views to create proxies
  /// </summary>
  /// <param name="views">current document named views</param>
  /// <returns></returns>
  public List<ViewProxy> UnpackViews(NamedViewTable views)
  {
    foreach (ViewInfo? view in views)
    {
      if (ViewProxies.ContainsKey(view.Name))
      {
        continue;
      }

      if (ConvertViewToViewProxy(view) is ViewProxy viewProxy)
      {
        ViewProxies.Add(view.Name, viewProxy);
      }
    }

    return ViewProxies.Values.ToList();
  }
}
