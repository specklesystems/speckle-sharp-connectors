using Autodesk.Revit.DB;

namespace Speckle.Connectors.Revit.HostApp;

/// <summary>
/// Collects design option elements from a Revit document. Mechanics only — the caller
/// decides whether to append and is responsible for deduplication.
/// </summary>
public class DesignOptionsHandler
{
  // Document-wide, not view-scoped: the setting means every option, whatever the view shows [ENG-10901].
  // View-independent only, as the categories filter: annotations inside an option belong to other views.
  public IReadOnlyList<Element> CollectDesignOptionElements(Document document)
  {
    using var optionCollector = new FilteredElementCollector(document);
    var optionIds = optionCollector.OfClass(typeof(DesignOption)).ToElementIds();

    var collected = new List<Element>();
    foreach (ElementId optionId in optionIds)
    {
      using var optionFilter = new ElementDesignOptionFilter(optionId);
      using var collector = new FilteredElementCollector(document);
      collected.AddRange(
        collector.WhereElementIsNotElementType().WhereElementIsViewIndependent().WherePasses(optionFilter)
      );
    }

    return collected;
  }
}
