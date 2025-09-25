using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Revit.HostApp;

namespace Speckle.Connectors.Revit.Operations.Send;

/// <summary>
/// Contains all the information needed for document processing in one place.
/// </summary>
public class ProcessedDocuments
{
  /// <summary>
  /// The main model document (non-linked), if any.
  /// </summary>
  public DocumentToConvert? MainModel { get; init; }

  /// <summary>
  /// Linked model instances grouped by document path.
  /// Key: Document path, Value: List of instances with different transforms.
  /// </summary>
  public Dictionary<string, List<DocumentToConvert>> LinkedModelGroups { get; init; } = [];

  /// <summary>
  /// Any validation results/warnings generated during processing.
  /// </summary>
  public List<SendConversionResult> ValidationResults { get; init; } = [];

  /// <summary>
  /// Check if we have any processable content.
  /// </summary>
  public bool HasProcessableContent =>
    (MainModel?.Elements.Count > 0) || LinkedModelGroups.Values.Any(group => group.Count != 0);

  /// <summary>
  /// Get total element count across all documents.
  /// </summary>
  public int TotalElementCount =>
    (MainModel?.Elements.Count ?? 0) + LinkedModelGroups.Values.Sum(group => group.Sum(doc => doc.Elements.Count));
}
