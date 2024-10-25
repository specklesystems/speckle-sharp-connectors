using Autodesk.Revit.DB;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.Utils;
using Speckle.Converters.RevitShared.Helpers;

namespace Speckle.Connectors.RevitShared.Operations.Send.Filters;

public class RevitViewsSelectionFilter : DiscriminatedObject, ISendFilter
{
  private RevitContext _revitContext;
  private Document? _doc;
  public string Id { get; set; } = "revitViewsSelection";
  public string Name { get; set; } = "Views Selection";
  public string? Summary { get; set; }
  public bool IsDefault { get; set; }
  public List<string> SelectedViewIds { get; set; } = new();

  public List<string> GetObjectIds()
  {
    var objectIds = new List<string>();
    if (_doc is null)
    {
      return objectIds;
    }

    if (SelectedViewIds.Count == 0)
    {
      return objectIds;
    }

    foreach (var viewId in SelectedViewIds)
    {
      objectIds.AddRange(GetObjectIdsFromView(viewId));
    }

    return objectIds;
  }

  private List<string> GetObjectIdsFromView(string viewUniqueId)
  {
    if (_doc is null)
    {
      return [];
    }

    var viewId = ElementIdHelper.GetElementIdFromUniqueId(_doc, viewUniqueId);
    using var viewCollector = new FilteredElementCollector(_doc, viewId);
    List<Element> elementsInView = viewCollector.ToElements().ToList(); // NOTE: "Floor Plans > Level 1" in sample model crashing if we use WhereElementIsNotElementType() function for viewCollector..
    return elementsInView.Select(e => e.UniqueId).ToList();
  }

  public void SetContext(RevitContext revitContext)
  {
    _revitContext = revitContext;
    _doc = _revitContext.UIApplication?.ActiveUIDocument.Document;
  }

  public bool CheckExpiry(string[] changedObjectIds) => SelectedViewIds.Intersect(changedObjectIds).Any();
}
