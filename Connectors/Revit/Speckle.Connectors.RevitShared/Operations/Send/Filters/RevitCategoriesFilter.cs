using Autodesk.Revit.DB;
using Speckle.Connectors.DUI.Exceptions;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.Utils;
using Speckle.Connectors.Revit.HostApp;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.RevitShared.Operations.Send.Filters;

public record CategoryData(string Name, string Id);

public class RevitCategoriesFilter : DiscriminatedObject, ISendFilter, IRevitSendFilter
{
  private RevitContext _revitContext;
  private Document? _doc;
  public string Id { get; set; } = "revitCategories";
  public string Name { get; set; } = "Categories";
  public string Type { get; set; } = "Custom";
  public string? Summary { get; set; }
  public bool IsDefault { get; set; }
  public List<string> SelectedObjectIds { get; set; } = new();
  public Dictionary<string, string>? IdMap { get; set; }
  public List<string>? SelectedCategories { get; set; }
  public List<CategoryData>? AvailableCategories { get; set; }

  public RevitCategoriesFilter() { }

  public RevitCategoriesFilter(RevitContext revitContext)
  {
    _revitContext = revitContext;
    _doc = _revitContext.UIApplication.NotNull().ActiveUIDocument.Document;

    GetCategories();
  }

  /// <summary>
  /// Always need to run on Revit UI thread (main) because of FilteredElementCollector.
  /// Use it with APIContext.Run
  /// </summary>
  /// <exception cref="SpeckleSendFilterException">Whenever no view is found.</exception>
  public List<string> RefreshObjectIds()
  {
    if (SelectedCategories is null)
    {
      return [];
    }

    // ⚠️ this is ugly, BUT we need to preserve RevitLinkInstances regardless of category.
    // these get unpacked later in the RefreshElementsIdsOnSender, so if we don't do this, they'll get filtered out here
    using var linkCollector = new FilteredElementCollector(_doc);
    var linkInstanceIds = linkCollector.OfClass(typeof(RevitLinkInstance)).Select(link => link.UniqueId).ToList();

    // get elements that match the selected categories (excluding RevitLinkInstance objects)
    var elementIds = SelectedCategories.Select(c => ElementIdHelper.GetElementId(c)).Where(e => e is not null).ToList();

    using var categoryFilter = new ElementMulticategoryFilter(elementIds);
    using var collector = new FilteredElementCollector(_doc);
    var elements = collector
      .WhereElementIsNotElementType()
      .WhereElementIsViewIndependent()
      .WherePasses(categoryFilter)
      .ToList();

    // combine both sets
    var objectIds = elements.Select(e => e.UniqueId).ToList();
    objectIds.AddRange(linkInstanceIds);

    SelectedObjectIds = objectIds;
    return objectIds;
  }

  private void GetCategories()
  {
    if (_doc is null)
    {
      return;
    }

    var categories = new List<CategoryData>();

    foreach (Category category in _doc.Settings.Categories)
    {
      if (SupportedCategoriesUtils.IsSupportedCategory(category)
#if REVIT2023_OR_GREATER
        && category.BuiltInCategory != BuiltInCategory.INVALID
#endif
      )
      {
        categories.Add(new CategoryData(category.Name, category.Id.ToString()));
      }
    }

    AvailableCategories = categories;
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
