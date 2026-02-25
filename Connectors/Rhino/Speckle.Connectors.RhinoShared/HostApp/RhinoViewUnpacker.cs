using Microsoft.Extensions.Logging;
using Rhino.DocObjects;
using Rhino.DocObjects.Tables;
using Speckle.Converters.Common;
using Speckle.Objects.Other;
using Speckle.Sdk;
using Speckle.Sdk.Pipelines.Progress;

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
  public List<Camera> UnpackViews(
    NamedViewTable views,
    IProgress<CardProgress> progress,
    CancellationToken cancellationToken
  )
  {
    int totalItemsToProcess = views.Count;
    int itemsProcessed = 0;

    List<Camera> cameras = new();
    foreach (ViewInfo view in views)
    {
      cancellationToken.ThrowIfCancellationRequested();

      progress.Report(
        new(
          $"Extracting views... ({itemsProcessed:N0} / {totalItemsToProcess:N0})",
          (double)itemsProcessed / totalItemsToProcess
        )
      );

      try
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
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogError(ex, "Failed to unpack {View}", view.Name);
      }
      finally
      {
        itemsProcessed++;
      }
    }

    return cameras;
  }
}
