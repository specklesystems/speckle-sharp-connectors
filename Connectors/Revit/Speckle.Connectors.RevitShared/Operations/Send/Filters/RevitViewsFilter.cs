using Autodesk.Revit.DB;
using Speckle.Connectors.DUI.Exceptions;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.Utils;
using Speckle.Converters.RevitShared.Extensions;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Sdk;

namespace Speckle.Connectors.RevitShared.Operations.Send.Filters;

public class RevitViewsFilter : DiscriminatedObject, ISendFilter, IRevitSendFilter
{
  private RevitContext _revitContext;
  public string Id { get; set; } = "revitViews";
  public string Name { get; set; } = "Views";
  public string Type { get; set; } = "Custom";
  public string? Summary { get; set; }
  public bool IsDefault { get; set; }
  public string? SelectedView { get; set; }
  public List<string> SelectedObjectIds { get; set; } = new();
  public Dictionary<string, string>? IdMap { get; set; } = new();
  public List<string>? AvailableViews { get; set; }

  public RevitViewsFilter() { }

  public RevitViewsFilter(RevitContext revitContext)
  {
    _revitContext = revitContext;
    var doc = _revitContext.UIApplication?.ActiveUIDocument?.Document;
    if (doc is not null)
    {
      GetViews(doc);
    }
  }

  public View? GetView(Document document)
  {
    if (SelectedView is null)
    {
      return null;
    }
    string[] result = SelectedView.Split(new string[] { " - " }, 2, StringSplitOptions.None);
    var viewFamilyString = result[0];
    var viewString = result[1];

    using var collector = new FilteredElementCollector(document);
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
    var document = _revitContext.UIApplication?.ActiveUIDocument?.Document;
    if (SelectedView is null || document is null)
    {
      return [];
    }

    // Paşa Bilal wants it like this... (three dots = important meaning for ogu)
    string[] result = SelectedView.Split([" - "], 2, StringSplitOptions.None);
    var viewFamilyString = result[0];
    var viewString = result[1];

    using var collector = new FilteredElementCollector(document);
    View? view = collector
      .OfClass(typeof(View))
      .Cast<View>()
      .FirstOrDefault(v => v.ViewType.ToString().Equals(viewFamilyString) && v.Name.Equals(viewString));

    if (view is null)
    {
      //this used to throw an exception, but we don't want to fail loudly if the view is not found
      return [];
    }

    IEnumerable<Element> elementsInView = GetFilteredElementsForView(document, view);

    // NOTE: FilteredElementCollector() includes sweeps and reveals from a wall family's definition and includes them as additional objects
    // on this return. displayValue for Wall already includes these, therefore we end up with duplicate elements on wall sweeps
    // related to [CNX-1482](https://linear.app/speckle/issue/CNX-1482/wall-sweeps-published-duplicated)
    // We filter only wall sweep/reveal sub-elements with empty names, not all unnamed elements,
    // because steel connection elements (plates, bolts) also have empty names and must be kept.
    // See [CNX-3130](https://linear.app/speckle/issue/CNX-3130)
    var objectIds = elementsInView
      .Where(e => !IsEmptyNameWallSubElement(e))
      .Select(e => e.UniqueId)
      .ToList();
    // we need the view uniqueId among the objectIds
    // to expire the modelCards with viewFilters when the user changes category visibility
    // a change in category visibility will trigger DocChangeHandler in RevitSendBinding
    // [CNX-914] https://linear.app/speckle/issue/CNX-914/hidingunhiding-a-category-dont-trigger-object-tracking
    objectIds.Add(view.UniqueId);
    SelectedObjectIds = objectIds;
    return objectIds;
  }

  private void GetViews(Document document)
  {
    using var collector = new FilteredElementCollector(document);
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
  }

  // NOTE: Element collector returns parts and source elements even when Parts Visibility is set as "Show Parts" only.
  // Below function collects list of ids to exclude from final list.
  private HashSet<ElementId> GetSourceElementIdsToExclude(IEnumerable<Element> elements)
  {
    var elementsToExclude = new HashSet<ElementId>();

    foreach (var element in elements)
    {
      // check if element is a part
      if (element.Category?.GetBuiltInCategory() == BuiltInCategory.OST_Parts && element is Part part)
      {
        try
        {
          // get source element ids from the part
          var sourceIds = part.GetSourceElementIds();
          if (sourceIds != null)
          {
            foreach (var sourceId in sourceIds)
            {
              elementsToExclude.Add(sourceId.HostElementId);
            }
          }
        }
        catch (Exception e) when (!e.IsFatal())
        {
          // silently continue processing other Parts if one fails
          // this follows the pattern used elsewhere in the codebase
        }
      }
    }
    return elementsToExclude;
  }

  private IEnumerable<Element> GetFilteredElementsForView(Document document, View view)
  {
    using var viewCollector = new FilteredElementCollector(document, view.Id);
    var allElements = viewCollector.ToElements();

    // parts filtering when view is set to show Parts only (and overwrites allElements)
    if (view.PartsVisibility == PartsVisibility.ShowPartsOnly)
    {
      var idsToExclude = GetSourceElementIdsToExclude(allElements);
      return allElements.Where(e => !idsToExclude.Contains(e.Id));
    }

    return allElements;
  }

  /// <summary>
  /// Detects wall sweep/reveal sub-elements that have empty names when returned by the
  /// view-scoped FilteredElementCollector. These are duplicates of geometry already included
  /// in the parent wall's displayValue.
  /// See <a href="https://linear.app/speckle/issue/CNX-1482">CNX-1482</a>.
  /// </summary>
  private static bool IsEmptyNameWallSubElement(Element e)
  {
    if (!string.IsNullOrEmpty(e.Name))
    {
      return false;
    }

    var bic = e.Category?.GetBuiltInCategory();
    return bic is BuiltInCategory.OST_Cornices or BuiltInCategory.OST_Reveals;
  }
}
