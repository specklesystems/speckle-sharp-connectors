using Autodesk.Revit.DB;
using Speckle.Connectors.DUI.Exceptions;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.Utils;
using Speckle.Connectors.Revit.HostApp;
using Speckle.Converters.RevitShared.Helpers;

namespace Speckle.Connectors.RevitShared.Operations.Send.Filters;

public record CategoryData(string Name, string Id);

public class RevitCategoriesFilter : DiscriminatedObject, ISendFilter, IRevitSendFilter
{
  private RevitContext _revitContext;
  private APIContext _apiContext;
  private Document? _doc;
  public string Id { get; set; } = "revitCategories";
  public string Name { get; set; } = "Categories";
  public string? Summary { get; set; }
  public bool IsDefault { get; set; }
  public List<string> ObjectIds { get; set; } = new();
  public Dictionary<string, string>? IdMap { get; set; }
  public List<string>? SelectedCategories { get; set; }
  public List<CategoryData>? AvailableCategories { get; set; }

  public RevitCategoriesFilter() { }

  public RevitCategoriesFilter(RevitContext revitContext, APIContext apiContext)
  {
    _revitContext = revitContext;
    _apiContext = apiContext;
    _doc = _revitContext.UIApplication?.ActiveUIDocument.Document;

    GetCategories();
  }

  /// <summary>
  /// Always need to run on Revit UI thread (main) because of FilteredElementCollector.
  /// Use it with APIContext.Run
  /// </summary>
  /// <exception cref="SpeckleSendFilterException">Whenever no view is found.</exception>
  public List<string> SetObjectIds()
  {
    var objectIds = new List<string>();
    if (SelectedCategories is null)
    {
      return objectIds;
    }

    var elementIds = SelectedCategories.Select(c => ElementIdHelper.GetElementId(c)).Where(e => e is not null).ToList();

    using var categoryFilter = new ElementMulticategoryFilter(elementIds);
    using var collector = new FilteredElementCollector(_doc);
    var elements = collector
      .WhereElementIsNotElementType()
      .WhereElementIsViewIndependent()
      .WherePasses(categoryFilter)
      .ToList();
    objectIds = elements.Select(e => e.UniqueId).ToList();
    ObjectIds = objectIds;
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
      categories.Add(new CategoryData(category.Name, category.Id.ToString()));
    }

    AvailableCategories = categories;
  }

  /// <summary>
  /// NOTE: this is needed since we need doc on `GetObjectIds()` function after it deserialized.
  /// DI doesn't help here to pass RevitContext from constructor.
  /// </summary>
  public void SetContext(RevitContext revitContext, APIContext apiContext)
  {
    _revitContext = revitContext;
    _apiContext = apiContext;
    _doc = _revitContext.UIApplication?.ActiveUIDocument.Document;
  }
}
