using System.IO;
using Autodesk.Revit.DB;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.RevitShared;
using Speckle.Connectors.RevitShared.Operations.Send.Filters;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Sdk;
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
  public List<Element> GetLinkedModelElements(ISendFilter sendFilter, Document linkedDocument, Transform? transform)
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
    if (sendFilter is RevitViewsFilter viewFilter && viewFilter.GetView(linkedDocument) != null)
    {
      RevitLinkInstance linkInstance = FindLinkInstanceForDocument(
        linkedDocument.PathName,
        _revitContext.UIApplication.NotNull().ActiveUIDocument.Document,
        transform
      );

#if REVIT2024_OR_GREATER
      var doc = _revitContext.UIApplication.ActiveUIDocument.Document;
      if (doc is null)
      {
        return new List<Element>();
      }
      // revit 2024 and 2025 we can use the three-parameter constructor to get only visible elements
      using var viewCollector = new FilteredElementCollector(
        doc,
        viewFilter.GetView(doc).NotNull().Id,
        linkInstance.Id
      );

      // NOTE: related to [CNX-1482](https://linear.app/speckle/issue/CNX-1482/wall-sweeps-published-duplicated). See RevitViewsFilter.cs
      return viewCollector.WhereElementIsNotElementType().Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
#else
      // 🚨 LIMITATION: in Revit 2023 and below, we can only check if the entire linked model is visible,
      // not individual elements within it. If the linked model is visible, all its elements will be included.
      // constructor overload pertaining to searching and filtering visible elements from a revit link only added 2024.
      if (linkInstance.IsHidden(viewFilter.GetView(linkedDocument).NotNull()))
      {
        return new List<Element>(); // if the linked model is hidden, return no elements
      }
      // 💩 fallback to getting all elements if the linked model is visible
      return GetAllElementsForLinkedModelSelection(linkedDocument);
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

  /// <summary>
  /// Finds a specific RevitLinkInstance that corresponds to a linked document with a matching transform.
  /// </summary>
  /// <param name="linkedDocumentPath">The file path of the linked document</param>
  /// <param name="transform">The transform to match (expected to already be an inverse transform).
  /// When provided with multiple instances of the same linked document, this is used to find the specific instance.</param>
  /// <param name="mainDocument">The main Revit document containing the link instances</param>
  /// <returns>The matching RevitLinkInstance, or the first available instance if no match is found</returns>
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

    // if no transform or only one instance, just return the first
    if (transform == null || linkInstances.Count <= 1)
    {
      return linkInstances.FirstOrDefault()
        ?? throw new SpeckleException($"No link instance found for {linkedDocumentPath}");
    }

    // a match consists of not only the linked document path name but the transformation too (think linked instances)
    // precompute our target hash once
    string targetHash = GetTransformHash(transform);

    // directly find the matching instance
    var matchingInstance = linkInstances.FirstOrDefault(link =>
      GetTransformHash(link.GetTotalTransform().Inverse) == targetHash
    );

    // return matching with a fallback to first (main) instance in case something goes funky with the hash
    return matchingInstance ?? linkInstances.First();
  }
}
