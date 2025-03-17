using Autodesk.Revit.DB;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.RevitShared;
using Speckle.Connectors.RevitShared.Operations.Send.Filters;

namespace Speckle.Connectors.Revit.HostApp;

/// <summary>
/// Handles unpacking elements inside linked models.
/// This class is responsible for the mechanics of retrieving elements from linked documents
/// based on different filter types, but not for making decisions about whether linked models
/// should be processed (which is the responsibility of the calling code)!
/// </summary>
public class LinkedModelHandler
{
  /// <summary>
  /// Gets elements from a linked document based on the provided send filter.
  /// This method handles the specifics of element collection but doesn't make decisions
  /// about whether the linked model should be processed - that's the caller's responsibility.
  /// </summary>
  public List<Element> GetLinkedModelElements(ISendFilter sendFilter, Document linkedDocument)
  {
    // send mode → Categories
    if (sendFilter is RevitCategoriesFilter categoryFilter && categoryFilter.SelectedCategories is not null)
    {
      var categoryIds = categoryFilter
        .SelectedCategories.Select(c => ElementIdHelper.GetElementId(c))
        .OfType<ElementId>()
        .ToList();

      if (categoryIds.Count > 0)
      {
        return GetElementsByCategory(linkedDocument, categoryIds);
      }
      return new List<Element>();
    }
    // send mode → Selection
    return GetAllElementsForLinkedModelSelection(linkedDocument);
  }

  /// <summary>
  /// Gets elements from a document that belong to the specified categories.
  /// </summary>
  private List<Element> GetElementsByCategory(Document linkedDoc, List<ElementId> categoryIds)
  {
    using var multicategoryFilter = new ElementMulticategoryFilter(categoryIds);
    using var collector = new FilteredElementCollector(linkedDoc);
    return collector
      .WhereElementIsNotElementType()
      .WhereElementIsViewIndependent()
      .WherePasses(multicategoryFilter)
      .ToList();
  }

  /// <summary>
  /// Retrieves all elements from the linked document when using selection filters.
  /// When a linked model is selected in the main document, we include all elements
  /// from that linked model since the selection is of the entire linked instance.
  /// </summary>
  private List<Element> GetAllElementsForLinkedModelSelection(Document linkedDoc)
  {
    using var collector = new FilteredElementCollector(linkedDoc);
    return collector.WhereElementIsNotElementType().WhereElementIsViewIndependent().ToList();
  }
}
