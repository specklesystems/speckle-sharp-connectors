using Autodesk.Revit.DB;
using Speckle.Connectors.DUI.Exceptions;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.Utils;
using Speckle.Converters.RevitShared.Helpers;

namespace Speckle.Connectors.RevitShared.Operations.Send.Filters;

public class RevitViewsFilter : DiscriminatedObject, ISendFilter, IRevitSendFilter
{
  private RevitContext _revitContext;
  private Document? _doc;
  public string Id { get; set; } = "revitViews";
  public string Name { get; set; } = "Views";
  public string Type { get; set; } = "Custom";
  public string? Summary { get; set; }
  public bool IsDefault { get; set; }
  public string? SelectedView { get; set; }
  public List<string> SelectedObjectIds { get; set; }
  public Dictionary<string, string>? IdMap { get; set; } = new();
  public List<string>? AvailableViews { get; set; }

  public RevitViewsFilter() { }

  public RevitViewsFilter(RevitContext revitContext)
  {
    _revitContext = revitContext;
    _doc = _revitContext.UIApplication?.ActiveUIDocument?.Document;

    GetViews();
  }

  public View? GetView()
  {
    if (SelectedView is null)
    {
      return null;
    }
    string[] result = SelectedView.Split(new string[] { " - " }, 2, StringSplitOptions.None);
    var viewFamilyString = result[0];
    var viewString = result[1];

    using var collector = new FilteredElementCollector(_doc);
    return collector
      .OfClass(typeof(View))
      .Cast<View>()
      .FirstOrDefault(v => v.ViewType.ToString().Equals(viewFamilyString) && v.Name.Equals(viewString));
  }

  /// <summary>
  /// Always need to run on Revit UI thread (main) because of FilteredElementCollector.
  /// Use it with APIContext.Run
  /// </summary>
  /// <exception cref="SpeckleSendFilterException">Whenever no view is found.</exception>
  public List<string> RefreshObjectIds()
  {
    if (SelectedView is null)
    {
      return [];
    }

    // Paşa Bilal wants it like this... (three dots = important meaning for ogu)
    string[] result = SelectedView.Split([" - "], 2, StringSplitOptions.None);
    var viewFamilyString = result[0];
    var viewString = result[1];

    using var collector = new FilteredElementCollector(_doc);
    View? view = collector
      .OfClass(typeof(View))
      .Cast<View>()
      .FirstOrDefault(v => v.ViewType.ToString().Equals(viewFamilyString) && v.Name.Equals(viewString));

    if (view is null)
    {
      //this used to throw an exception but we don't want to fail loudly if the view is not found
      return [];
    }
    using var viewCollector = new FilteredElementCollector(_doc, view.Id);
    var elementsInView = viewCollector.ToElements();
    var objectIds = elementsInView.Where(e => e.Category?.Parent == null).Select(e => e.UniqueId).ToList();
    SelectedObjectIds = objectIds;
    return objectIds;
  }

  private void GetViews()
  {
    using var collector = new FilteredElementCollector(_doc);
    var views = collector
      .OfClass(typeof(View))
      .Cast<View>()
      .Where(v => !v.IsTemplate)
      .Where(v => !v.IsAssemblyView)
      .Where(v =>
        v.ViewType
          is ViewType.FloorPlan
            or ViewType.Elevation
            or ViewType.Rendering
            or ViewType.Section
            or ViewType.ThreeD
            or ViewType.Detail
            or ViewType.CeilingPlan
            or ViewType.AreaPlan
      )
      .Select(v => v.ViewType.ToString() + " - " + v.Name.ToString())
      .ToList();
    AvailableViews = views;
  }

  /// <summary>
  /// NOTE: this is needed since we need doc on `GetObjectIds()` function after it deserialized.
  /// DI doesn't help here to pass RevitContext from constructor.
  /// </summary>
  public void SetContext(RevitContext revitContext)
  {
    _revitContext = revitContext;
    _doc = _revitContext.UIApplication?.ActiveUIDocument.Document;
  }
}
