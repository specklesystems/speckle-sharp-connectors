using System.IO;
using Autodesk.Revit.DB;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.RevitShared;
using Speckle.Connectors.RevitShared.Operations.Send.Filters;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.Revit.HostApp;

/// <summary>
/// Handles unpacking elements inside linked models.
/// This class is responsible for the mechanics of retrieving elements from linked documents
/// based on different filter types, but not for making decisions about whether linked models
/// should be processed (which is the responsibility of the calling code)!
/// </summary>
public class LinkedModelHandler
{
  private readonly RevitContext _revitContext;
  public Dictionary<string, string> LinkedModelDisplayNames { get; } = new();

  public LinkedModelHandler(RevitContext revitContext)
  {
    _revitContext = revitContext;
  }

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

    // send mode → Views (taken from the legacy code)
    if (sendFilter is RevitViewsFilter viewFilter && viewFilter.GetView() != null)
    {
#if REVIT2024_OR_GREATER
      // revit 2024 and 2025 we can use the three-parameter constructor to get only visible elements
      RevitLinkInstance linkInstance = FindLinkInstanceForDocument(
        linkedDocument,
        _revitContext.UIApplication.NotNull().ActiveUIDocument.Document
      );
      using var viewCollector = new FilteredElementCollector(
        _revitContext.UIApplication.ActiveUIDocument.Document,
        viewFilter.GetView().NotNull().Id,
        linkInstance.Id
      );
      return viewCollector.WhereElementIsNotElementType().ToElements().ToList();
#else
      // revit 2023 and below, we can only check if the entire linked model is visible
      RevitLinkInstance linkInstance = FindLinkInstanceForDocument(
        linkedDocument,
        _revitContext.UIApplication.ActiveUIDocument.Document
      );
      if (linkInstance.IsHidden(viewFilter.GetView().NotNull()))
      {
        return new List<Element>(); // If the linked model is hidden, return no elements
      }
      // Fallback to getting all elements if the linked model is visible
      using var collector = new FilteredElementCollector(linkedDocument);
      return collector.WhereElementIsNotElementType().WhereElementIsViewIndependent().ToList();
#endif
    }

    // send mode → Selection
    return GetAllElementsForLinkedModelSelection(linkedDocument);
  }

  /// <summary>
  /// Prepares display names for linked model documents based on filename
  /// </summary>
  public void PrepareLinkedModelNames(IReadOnlyList<DocumentToConvert> documentElementContexts)
  {
    LinkedModelDisplayNames.Clear();
    // Group linked models by filename
    var linkedModels = documentElementContexts
      .Where(ctx => ctx.Doc.IsLinked)
      .GroupBy(ctx => Path.GetFileNameWithoutExtension(ctx.Doc.PathName))
      .ToDictionary(g => g.Key, g => g.ToList());

    // Create a unique key for each instance
    foreach (var group in linkedModels)
    {
      string baseName = group.Key;
      var instances = group.Value;

      // Single instance - just use the base name
      if (instances.Count == 1)
      {
        string id = GetIdFromDocumentToConvert(instances[0]);
        LinkedModelDisplayNames[id] = baseName;
      }
      // Multiple instances - add numbering
      else
      {
        for (int i = 0; i < instances.Count; i++)
        {
          string id = GetIdFromDocumentToConvert(instances[i]);
          LinkedModelDisplayNames[id] = $"{baseName}_{i + 1}";
        }
      }
    }
  }

  public string GetIdFromDocumentToConvert(DocumentToConvert documentToConvert) =>
    documentToConvert.Doc.GetHashCode() + "-" + (documentToConvert.Transform?.GetHashCode() ?? 0);

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

  // Helper method to generate a simple hash for a transform
  // transformedElement.applicationId = ${applicationId}-t{transformHash}
  public string GetTransformHash(Transform transform)
  {
    // create a simplified representation of the transform
    string json =
      $@"{{
      ""origin"": [{transform.Origin.X:F2}, {transform.Origin.Y:F2}, {transform.Origin.Z:F2}],
      ""basis"": [{transform.BasisX.X:F1}, {transform.BasisY.Y:F1}, {transform.BasisZ.Z:F1}]
    }}";

    byte[] jsonBytes = System.Text.Encoding.UTF8.GetBytes(json);

#pragma warning disable CA1850
    using (var sha256 = System.Security.Cryptography.SHA256.Create())
    {
      byte[] hashBytes = sha256.ComputeHash(jsonBytes);
      // keep only the first 8 characters for a short but unique hash
      return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant()[..8];
    }
#pragma warning restore CA1850
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

  private RevitLinkInstance FindLinkInstanceForDocument(Document linkedDocument, Document mainDocument)
  {
    using var collector = new FilteredElementCollector(mainDocument);
    return collector
      .OfClass(typeof(RevitLinkInstance))
      .Cast<RevitLinkInstance>()
      .FirstOrDefault(link => link.GetLinkDocument()?.PathName == linkedDocument.PathName)
      .NotNull();
  }
}
