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

  private Dictionary<ElementId, ViewProxy> ViewProxies { get; } = new();

  private ViewProxy? ConvertViewToViewProxy(View3D view)
  {
    try
    {
      var converted = (Camera)_rootToSpeckleConverter.Convert(view);
      if (converted != null)
      {
        return new()
        {
          name = view.Title,
          value = converted,
          applicationId = view.Id.ToString(),
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
  /// Iterates through the 3D views in the provided document to create view proxies
  /// </summary>
  /// <param name="doc">Document to retrieve 3D views from</param>
  /// <returns></returns>
  public List<ViewProxy> Unpack(Document doc)
  {
    using FilteredElementCollector collector = new(doc);
    List<View> views = collector
      .WhereElementIsNotElementType()
      .OfCategory(BuiltInCategory.OST_Views)
      .Cast<View>()
      .Where(x => x.ViewType == ViewType.ThreeD)
      .ToList();

    foreach (View view in views)
    {
      if (ViewProxies.ContainsKey(view.Id) || view is not View3D view3D)
      {
        continue;
      }

      if (ConvertViewToViewProxy(view3D) is ViewProxy viewProxy)
      {
        ViewProxies.Add(view.Id, viewProxy);
      }
    }

    return ViewProxies.Values.ToList();
  }
}
