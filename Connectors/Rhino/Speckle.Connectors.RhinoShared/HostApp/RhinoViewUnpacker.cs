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

  private Camera? ConvertViewToCamera(ViewInfo view)
  {
    try
    {
      var converted = (Speckle.Objects.Other.Camera)_rootToSpeckleConverter.Convert(view);
      if (converted is null)
      {
        return null;
      }

      return converted;
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, "Failed to create a view from {view}", view.Name);
      return null;
    }
  }

  /// <summary>
  /// Iterates through a given set of rhino named views to create proxies
  /// </summary>
  /// <param name="views">current document named views</param>
  /// <returns></returns>
  public List<Camera> UnpackViews(NamedViewTable views)
  {
    List<Camera> cameras = new();
    foreach (ViewInfo view in views)
    {
      // skip isometric views for now.
      // getting the orthographic match between host apps and the viewer requires too much effort atm.
      if (view.Viewport.IsParallelProjection)
      {
        continue;
      }

      if (ConvertViewToCamera(view) is Camera camera)
      {
        cameras.Add(camera);
      }
    }

    return cameras;
  }
}
