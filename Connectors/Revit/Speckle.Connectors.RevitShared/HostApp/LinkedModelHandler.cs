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
/// Handles linked model document operations: grouping, element collection, and display name management.
/// Focused on linked model domain logic without conversion or proxy concerns.
/// </summary>
public class LinkedModelDocumentHandler
{
  private readonly RevitContext _revitContext;
  public Dictionary<string, string> LinkedModelDisplayNames { get; } = new();

  public LinkedModelDocumentHandler(RevitContext revitContext)
  {
    _revitContext = revitContext;
  }

  /// <summary>
  /// Groups documents by their unique models and prepares display names.
  /// This is the main entry point for organizing linked model documents.
  /// </summary>
  /// <param name="documents">All documents to process</param>
  /// <returns>Organized document groups</returns>
  public DocumentGroups GroupAndPrepareDocuments(IReadOnlyList<DocumentToConvert> documents)
  {
    var (mainModel, linkedGroups) = GroupDocumentsByUniqueModels(documents);

    if (linkedGroups.Count > 0)
    {
      PrepareLinkedModelDisplayNames(documents);
    }

    return new DocumentGroups(mainModel, linkedGroups);
  }

  /// <summary>
  /// Groups documents by their unique linked models, separating main models from linked models.
  /// This helps identify which linked models are the same (same file) but in different positions.
  /// </summary>
  /// <param name="documents">All documents to process</param>
  /// <returns>Main model and grouped linked model instances</returns>
  public (
    DocumentToConvert? MainModel,
    Dictionary<string, List<DocumentToConvert>> LinkedModelInstances
  ) GroupDocumentsByUniqueModels(IReadOnlyList<DocumentToConvert> documents)
  {
    DocumentToConvert? mainModel = null;
    var linkedModelInstances = new Dictionary<string, List<DocumentToConvert>>();

    foreach (var document in documents)
    {
      if (document == null)
      {
        continue;
      }

      if (document.Doc.IsLinked)
      {
        // group linked models by their document path (same model file, different transforms)
        string documentPathName = document.Doc.PathName;

        if (!linkedModelInstances.TryGetValue(documentPathName, out List<DocumentToConvert>? instances))
        {
          instances = [];
          linkedModelInstances[documentPathName] = instances;
        }
        instances.Add(document);
      }
      else
      {
        mainModel = document;
      }
    }

    return (mainModel, linkedModelInstances);
  }

  /// <summary>
  /// Gets elements from a linked document based on the provided send filter.
  /// This method handles the specifics of element collection but doesn't make decisions
  /// about whether the linked model should be processed - that's the caller's responsibility.
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
  /// Creates a unique identifier for a DocumentToConvert that includes transform information.
  /// </summary>
  public string GetIdFromDocumentToConvert(DocumentToConvert documentToConvert)
  {
    var docHash = documentToConvert.Doc.GetHashCode();
    var transformHash = documentToConvert.Transform?.GetHashCode() ?? 0;
    return $"{docHash}-{transformHash}";
  }

  /// <summary>
  /// Prepares display names for linked model documents based on filename.
  /// Handles naming conflicts when multiple instances of the same model exist.
  /// </summary>
  private void PrepareLinkedModelDisplayNames(IReadOnlyList<DocumentToConvert> documentElementContexts)
  {
    LinkedModelDisplayNames.Clear();

    // group linked models by filename
    var linkedModels = documentElementContexts
      .Where(ctx => ctx?.Doc.IsLinked == true)
      .GroupBy(ctx => Path.GetFileNameWithoutExtension(ctx!.Doc.PathName))
      .ToDictionary(g => g.Key, g => g.ToList()!);

    // create unique display names for each instance
    foreach (var group in linkedModels)
    {
      string baseName = group.Key;
      var instances = group.Value;

      if (instances.Count == 1)
      {
        // single instance - just use the base name
        string id = GetIdFromDocumentToConvert(instances[0]);
        LinkedModelDisplayNames[id] = baseName;
      }
      else
      {
        // multiple instances - add numbering
        for (int i = 0; i < instances.Count; i++)
        {
          string id = GetIdFromDocumentToConvert(instances[i]);
          LinkedModelDisplayNames[id] = $"{baseName}_{i + 1}";
        }
      }
    }
  }

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
    string targetHash = TransformUtils.ComputeTransformHash(transform);
    var matchingInstance = linkInstances.FirstOrDefault(link =>
      TransformUtils.ComputeTransformHash(link.GetTotalTransform().Inverse) == targetHash
    );

    return matchingInstance ?? linkInstances.First();
  }
}

/// <summary>
/// Data structure for organizing documents by type (main vs linked models).
/// </summary>
public record DocumentGroups(
  DocumentToConvert? MainModel,
  Dictionary<string, List<DocumentToConvert>> LinkedModelGroups
);
