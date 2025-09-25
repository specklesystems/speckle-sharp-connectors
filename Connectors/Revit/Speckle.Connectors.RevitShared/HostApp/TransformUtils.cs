using System.IO;
using Autodesk.Revit.DB;

namespace Speckle.Connectors.Revit.HostApp;

/// <summary>
/// Simplified utility for generating unique IDs for linked models and instances.
/// </summary>
public static class TransformUtils
{
  /// <summary>
  /// Creates a unique definition ID for a linked model based on its document path.
  /// Same linked model always gets the same definition ID.
  /// </summary>
  /// <returns>Unique definition ID like "LinkedModel_Building_A_1234"</returns>
  public static string CreateDefinitionId(string documentPath)
  {
    var fileName = Path.GetFileNameWithoutExtension(documentPath);
    var hash = CreateSimpleHash(documentPath);
    return $"LinkedModel_{fileName}_{hash}";
  }

  /// <summary>
  /// Creates a unique instance ID for a specific positioned instance of a linked model.
  /// </summary>
  /// <param name="definitionId">The definition ID from CreateDefinitionId</param>
  /// <param name="instanceIndex">1-based index of this instance</param>
  /// <returns>Unique instance ID like "LinkedModel_Building_A_1234_instance_1"</returns>
  public static string CreateInstanceId(string definitionId, int instanceIndex) =>
    $"{definitionId}_instance_{instanceIndex}";

  /// <summary>
  /// Creates a simple hash for transform identification.
  /// Used to distinguish different positioned instances of the same linked model.
  /// </summary>
  /// <param name="transform">The transform to hash</param>
  /// <returns>8-character hex hash for quick comparison</returns>
  public static string CreateTransformHash(Transform transform)
  {
    // Create simplified transform representation
    var transformData =
      $"{transform.Origin.X:F2},{transform.Origin.Y:F2},{transform.Origin.Z:F2},"
      + $"{transform.BasisX.X:F1},{transform.BasisY.Y:F1},{transform.BasisZ.Z:F1}";

    return CreateSimpleHash(transformData);
  }

  /// <summary>
  /// Creates a consistent 8-character hash from input string.
  /// Using built-in GetHashCode for simplicity - adequate for our use case.
  /// </summary>
  private static string CreateSimpleHash(string input)
  {
    // Use built-in hash code - much simpler than SHA256 for this use case
    var hash = input.GetHashCode();
    return Math.Abs(hash).ToString("X8"); // 8-char hex string
  }
}

// Extension method to make usage cleaner
public static class DocumentToConvertExtensions
{
  /// <summary>
  /// Gets a simple display name for the document.
  /// </summary>
  public static string GetDisplayName(this DocumentToConvert document) =>
    Path.GetFileNameWithoutExtension(document.Doc.PathName);
}
