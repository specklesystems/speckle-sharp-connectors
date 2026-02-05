using Autodesk.Revit.DB;
using Microsoft.Extensions.Logging;
using Speckle.Objects.Other;
using Speckle.Sdk;

namespace Speckle.Connectors.Revit.HostApp;

/// <summary>
/// Unpacks Revit Views for sending
/// </summary>
public class ViewUnpacker
{
  private readonly ILogger<ViewUnpacker> _logger;
  private readonly Converters.Common.IRootToSpeckleConverter _rootToSpeckleConverter;

  public ViewUnpacker(Converters.Common.IRootToSpeckleConverter rootToSpeckleConverter, ILogger<ViewUnpacker> logger)
  {
    _rootToSpeckleConverter = rootToSpeckleConverter;
    _logger = logger;
  }

  private Camera? ConvertViewToCamera(View3D view)
  {
    try
    {
      var converted = (Camera)_rootToSpeckleConverter.Convert(view);
      if (converted is null)
      {
        _logger.LogError("Failed to create a view from {view}", view.Name);
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
  /// Iterates through the 3D views in the provided document to create cameras
  /// </summary>
  /// <param name="doc">Document to retrieve 3D views from</param>
  /// <returns></returns>
  public List<Camera> Unpack(Document doc)
  {
    List<Camera> cameras = new();
    using FilteredElementCollector collector = new(doc);
    List<View> views = collector
      .WhereElementIsNotElementType()
      .OfCategory(BuiltInCategory.OST_Views)
      .Cast<View>()
      .Where(x => x.ViewType == ViewType.ThreeD)
      .ToList();

    foreach (View view in views)
    {
      if (view is not View3D view3D)
      {
        continue;
      }

      // not supporting parallel project yet, since it is too complex to match in the viewer for now
      try
      {
        if (!view3D.IsPerspective)
        {
          continue;
        }
      }
      catch (Autodesk.Revit.Exceptions.InvalidOperationException)
      {
        continue; // some threed views will throw an exception: returns true if view is not a view template
      }

      if (ConvertViewToCamera(view3D) is Camera camera)
      {
        cameras.Add(camera);
      }
    }

    return cameras;
  }
}
