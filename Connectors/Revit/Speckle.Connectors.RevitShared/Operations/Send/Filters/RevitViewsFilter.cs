using Autodesk.Revit.DB;
using Speckle.Connectors.DUI.Exceptions;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.Utils;
using Speckle.Converters.RevitShared.Helpers;

namespace Speckle.Connectors.RevitShared.Operations.Send.Filters;

public class RevitViewsFilter : DiscriminatedObject, ISendFilter
{
  private RevitContext _revitContext;
  private Document? _doc;
  public string Id { get; set; } = "revitViews";
  public string Name { get; set; } = "Views";
  public string? Summary { get; set; }
  public bool IsDefault { get; set; }
  public string? SelectedView { get; set; }
  public List<string>? AvailableViews { get; set; }

  public RevitViewsFilter() { }

  public RevitViewsFilter(RevitContext revitContext)
  {
    _revitContext = revitContext;
    _doc = _revitContext.UIApplication?.ActiveUIDocument.Document;
    GetViews();
  }

  public List<string> GetObjectIds()
  {
    var objectIds = new List<string>();
    if (SelectedView is null)
    {
      return objectIds;
    }

    // Paşa Bilal wants it like this..
    string[] result = SelectedView.Split(new string[] { " - " }, 2, StringSplitOptions.None);
    var viewFamilyString = result[0];
    var viewString = result[1];

    using var collector = new FilteredElementCollector(_doc);
    View? view = collector
      .OfClass(typeof(View))
      .Cast<View>()
      .FirstOrDefault(v => v.ViewType.ToString().Equals(viewFamilyString) && v.Name.Equals(viewString));

    if (view is null)
    {
      throw new SpeckleSendFilterException("View not found, please update your model send filter.");
    }
    using var viewCollector = new FilteredElementCollector(_doc, view.Id);
    List<Element> elementsInView = viewCollector.ToElements().ToList();

    return elementsInView.Select(e => e.UniqueId).ToList();
  }

  public bool CheckExpiry(string[] changedObjectIds) => GetObjectIds().Intersect(changedObjectIds).Any();

  private void GetViews()
  {
    using var collector = new FilteredElementCollector(_doc);
    var views = collector
      .OfClass(typeof(View))
      .Cast<View>()
      .Where(v => !v.IsTemplate)
      .Select(v => v.ViewType.ToString() + " - " + v.Name.ToString())
      .ToList();
    AvailableViews = views;
  }

  public void SetContext(RevitContext revitContext)
  {
    _revitContext = revitContext;
    _doc = _revitContext.UIApplication?.ActiveUIDocument.Document;
  }
}
