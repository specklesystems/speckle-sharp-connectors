using Autodesk.Revit.DB;
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
  public string? SelectedViewDiscipline { get; set; }
  public string? SelectedViewFamily { get; set; }
  public string? SelectedView { get; set; }
  public List<string>? AvailableDisciplines { get; set; }
  public Dictionary<string, List<string>>? AvailableViews { get; set; }

  public RevitViewsFilter() { }

  public RevitViewsFilter(RevitContext revitContext)
  {
    _revitContext = revitContext;
    _doc = _revitContext.UIApplication?.ActiveUIDocument.Document;
    GetAvailableDisciplines();
    GetViews();
  }

  public List<string> GetObjectIds()
  {
    var objectIds = new List<string>();
    if (SelectedView is null)
    {
      return objectIds;
    }

    using var collector = new FilteredElementCollector(_doc);
    View? view = collector
      .OfClass(typeof(View))
      .Cast<View>()
      .FirstOrDefault(v => v.Name.Equals(SelectedView, StringComparison.OrdinalIgnoreCase));

    if (view is null)
    {
      return objectIds;
    }
    using var viewCollector = new FilteredElementCollector(_doc, view.Id);
    List<Element> elementsInView = viewCollector.WhereElementIsNotElementType().ToList();

    return elementsInView.Select(e => e.UniqueId).ToList();
  }

  public bool CheckExpiry(string[] changedObjectIds) => GetObjectIds().Intersect(changedObjectIds).Any();

  private void GetAvailableDisciplines() => AvailableDisciplines = Enum.GetNames(typeof(ViewDiscipline)).ToList();

  private void GetViews()
  {
    using var collector = new FilteredElementCollector(_doc);
    var views = collector
      .OfClass(typeof(View))
      .Cast<View>()
      .Where(v => !v.IsTemplate)
      .GroupBy(v => v.ViewType)
      .ToDictionary(
        g => g.Key.ToString(), // Key is the view type as a string
        g => g.Select(v => v.Name).ToList() // Values are the view names as string[]
      );
    AvailableViews = views;
  }

  public void SetContext(RevitContext revitContext)
  {
    _revitContext = revitContext;
    _doc = _revitContext.UIApplication?.ActiveUIDocument.Document;
  }
}
