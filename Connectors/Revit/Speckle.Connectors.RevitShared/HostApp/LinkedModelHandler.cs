using Autodesk.Revit.DB;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.RevitShared;
using Speckle.Connectors.RevitShared.Operations.Send.Filters;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Sdk;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.Revit.HostApp;

/// <summary>
/// Handles linked model document operations
/// </summary>
public class LinkedModelDocumentHandler
{
  private readonly RevitContext _revitContext;

  public LinkedModelDocumentHandler(RevitContext revitContext)
  {
    _revitContext = revitContext;
  }

  /// <summary>
  /// Gets elements from a linked document based on the provided send filter.
  /// </summary>
  public List<Element> GetLinkedModelElements(ISendFilter sendFilter, Document linkedDocument, Transform? transform) =>
    sendFilter switch
    {
      RevitCategoriesFilter categoryFilter when categoryFilter.SelectedCategories is not null
        => GetElementsByCategory(linkedDocument, categoryFilter),
      RevitViewsFilter viewFilter when viewFilter.GetView() != null
        => GetElementsFromView(linkedDocument, viewFilter, transform),
      _ => GetAllElementsForLinkedModelSelection(linkedDocument)
    };

  /// <summary>
  /// Gets elements from a document that belong to the specified categories.
  /// </summary>
  private List<Element> GetElementsByCategory(Document linkedDoc, RevitCategoriesFilter categoryFilter)
  {
    var categoryIds = categoryFilter
      .SelectedCategories!.Select(c => ElementIdHelper.GetElementId(c))
      .OfType<ElementId>()
      .ToList();

    if (categoryIds.Count == 0)
    {
      return new List<Element>();
    }

    using var multicategoryFilter = new ElementMulticategoryFilter(categoryIds);
    using var collector = new FilteredElementCollector(linkedDoc);
    return collector
      .WhereElementIsNotElementType()
      .WhereElementIsViewIndependent()
      .WherePasses(multicategoryFilter)
      .ToList();
  }

  /// <summary>
  /// Gets elements from a linked document visible in a specific view.
  /// </summary>
  private List<Element> GetElementsFromView(Document linkedDocument, RevitViewsFilter viewFilter, Transform? transform)
  {
    var view = viewFilter.GetView()!;
    var linkInstance = FindLinkInstanceForDocument(
      linkedDocument.PathName,
      _revitContext.UIApplication.NotNull().ActiveUIDocument.Document,
      transform
    );

#if REVIT2024_OR_GREATER
    // Revit 2024+ can filter visible elements in linked models
    using var viewCollector = new FilteredElementCollector(
      _revitContext.UIApplication.ActiveUIDocument.Document,
      view.Id,
      linkInstance.Id
    );

    return viewCollector.WhereElementIsNotElementType().Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
#else
    // Revit 2023 and below limitation - check if entire linked model is visible
    if (linkInstance.IsHidden(view))
    {
      return new List<Element>();
    }
    return GetAllElementsForLinkedModelSelection(linkedDocument);
#endif
  }

  /// <summary>
  /// Retrieves all elements from the linked document when using selection filters.
  /// </summary>
  private List<Element> GetAllElementsForLinkedModelSelection(Document linkedDoc)
  {
    using var collector = new FilteredElementCollector(linkedDoc);
    return collector.WhereElementIsNotElementType().WhereElementIsViewIndependent().ToList();
  }

  /// <summary>
  /// Finds a specific RevitLinkInstance that corresponds to a linked document with a matching transform.
  /// </summary>
  private RevitLinkInstance FindLinkInstanceForDocument(
    string linkedDocumentPath,
    Document mainDocument,
    Transform? transform
  )
  {
    using var collector = new FilteredElementCollector(mainDocument);
    var linkInstances = collector
      .OfClass(typeof(RevitLinkInstance))
      .Cast<RevitLinkInstance>()
      .Where(link => link.GetLinkDocument()?.PathName == linkedDocumentPath)
      .ToList();

    // if no transform or only one instance, return the first
    if (transform == null || linkInstances.Count <= 1)
    {
      return linkInstances.FirstOrDefault()
        ?? throw new SpeckleException($"No link instance found for {linkedDocumentPath}");
    }

    // find matching instance by transform hash
    string targetHash = TransformUtils.CreateTransformHash(transform);
    var matchingInstance = linkInstances.FirstOrDefault(link =>
      TransformUtils.CreateTransformHash(link.GetTotalTransform().Inverse) == targetHash
    );

    return matchingInstance ?? linkInstances.First();
  }
}
