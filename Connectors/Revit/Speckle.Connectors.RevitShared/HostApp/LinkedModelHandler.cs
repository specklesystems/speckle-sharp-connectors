using System.IO;
using System.Security.Cryptography;
using Autodesk.Revit.DB;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.RevitShared;
using Speckle.Connectors.RevitShared.Operations.Send.Filters;
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
  public Dictionary<string, string> LinkedModelDisplayNames { get; } = new();

  /// <summary>
  /// Gets elements from a linked document based on the provided send filter.
  /// This method handles the specifics of element collection but doesn't make decisions
  /// about whether the linked model should be processed - that's the caller's responsibility.
  /// </summary>
  public List<Element> GetLinkedModelElements(
    Document currentDocument,
    ISendFilter sendFilter,
    Document linkedDocument,
    RevitLinkInstance linkInstance
  )
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
    if (sendFilter is RevitViewsFilter viewFilter && viewFilter.GetView(currentDocument) != null)
    {
#if REVIT2024_OR_GREATER
      // revit 2024 and 2025 we can use the three-parameter constructor to get only visible elements
      using var viewCollector = new FilteredElementCollector(
        currentDocument,
        viewFilter.GetView(currentDocument).NotNull().Id,
        linkInstance.Id
      );

      // NOTE: related to [CNX-1482](https://linear.app/speckle/issue/CNX-1482/wall-sweeps-published-duplicated). See RevitViewsFilter.cs
      return viewCollector.WhereElementIsNotElementType().Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
#else
      // 🚨 LIMITATION: in Revit 2023 and below, we can only check if the entire linked model is visible,
      // not individual elements within it. If the linked model is visible, all its elements will be included.
      // constructor overload pertaining to searching and filtering visible elements from a revit link only added 2024.
      if (linkInstance.IsHidden(viewFilter.GetView(currentDocument).NotNull()))
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

  /// <summary>
  /// The identity of one linked-model placement: the placing <see cref="RevitLinkInstance"/>'s UniqueId, or
  /// <c>host</c> for the host document. Unique by construction, stable across sessions, traceable back to a
  /// real Revit element.
  /// </summary>
  public string GetIdFromDocumentToConvert(DocumentToConvert documentToConvert) =>
    documentToConvert.LinkInstance?.UniqueId ?? "host";

  /// <summary>
  /// The <c>_t{hash}</c> suffix appended to every source UniqueId of a linked-model placement, so occurrences of
  /// the same linked file under different placements stay distinct identities. Empty for the host document.
  /// </summary>
  /// <remarks>
  /// <para>
  /// Derived from the placing link instance, NOT from the placement transform. The transform hash it replaces
  /// summarised a 3×3 basis by its three diagonal terms at one decimal place, discarding the off-diagonal terms —
  /// exactly what separates one rotation from another. Two placements of the same link at a shared origin rotated
  /// +90° and −90° hashed identically, and both then interned to a single object, silently losing one occurrence.
  /// </para>
  /// <para>
  /// Keyed off <see cref="DocumentToConvert.LinkInstance"/> rather than "has a transform": the HOST document also
  /// carries a Transform whenever the reference-point setting is Project Base, Survey or Shared Coordinates, so the
  /// old test suffixed every host element too — which made the parameter editor refuse them as linked
  /// (<c>ContainsLinkedModelTransformHash</c>) and rewrote every application ID whenever the setting changed.
  /// </para>
  /// <para>
  /// Still a hash, and still <c>_t</c> + lowercase hex, so the <c>_t[a-f0-9]+$</c> probe in
  /// <c>RevitParametersBinding</c> and the suffix-stripping convention both keep working — a raw UniqueId contains
  /// hyphens and would break them.
  /// </para>
  /// </remarks>
  public string GetPlacementSuffix(DocumentToConvert documentToConvert)
  {
    if (documentToConvert.LinkInstance is not { } linkInstance)
    {
      return string.Empty;
    }

    byte[] bytes = System.Text.Encoding.UTF8.GetBytes(linkInstance.UniqueId);
#if NET8_0_OR_GREATER
    return "_t" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()[..16];
#else
    using (var sha256 = SHA256.Create())
    {
      return "_t" + BitConverter.ToString(sha256.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant()[..16];
    }
#endif
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
