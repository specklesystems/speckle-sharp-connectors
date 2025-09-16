using System.IO;
using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.DB;

namespace Speckle.Connectors.Revit.HostApp;

public static class TransformUtils
{
  /// <summary>
  /// Generates a consistent hash for a transform to identify unique transformations.
  /// Used for distinguishing different instances of the same linked model.
  /// </summary>
  /// <param name="transform">The transform to hash</param>
  /// <returns>An 8-character lowercase hexadecimal hash</returns>
  public static string ComputeTransformHash(Transform transform)
  {
    // Create a simplified representation of the transform
    string json =
      $@"{{
      ""origin"": [{transform.Origin.X:F2}, {transform.Origin.Y:F2}, {transform.Origin.Z:F2}],
      ""basis"": [{transform.BasisX.X:F1}, {transform.BasisY.Y:F1}, {transform.BasisZ.Z:F1}]
    }}";

    byte[] jsonBytes = Encoding.UTF8.GetBytes(json);

#pragma warning disable CA1850
    using (var sha256 = SHA256.Create())
    {
      byte[] hashBytes = sha256.ComputeHash(jsonBytes);
      // Take first 8 characters for a shorter but still unique hash
      return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant()[..8];
    }
#pragma warning restore CA1850
  }

  /// <summary>
  /// Generates a consistent definition ID for a linked model based on its document path.
  /// Ensures the same linked model always gets the same definition ID across different projects.
  /// </summary>
  /// <param name="documentPath">The full path to the linked model document</param>
  /// <returns>A unique definition ID in the format "LinkedModel_{fileName}_{hash}"</returns>
  public static string GenerateDefinitionId(string documentPath)
  {
    string fileName = Path.GetFileNameWithoutExtension(documentPath);
    string hash = ComputeSimpleHash(documentPath);
    return $"LinkedModel_{fileName}_{hash}";
  }

  /// <summary>
  /// Computes a simple hash of a string for generating consistent IDs.
  /// </summary>
  /// <param name="input">The input string to hash</param>
  /// <returns>An 8-character lowercase hexadecimal hash</returns>
  private static string ComputeSimpleHash(string input)
  {
    byte[] inputBytes = Encoding.UTF8.GetBytes(input);

#pragma warning disable CA1850
    using (var sha256 = SHA256.Create())
    {
      byte[] hashBytes = sha256.ComputeHash(inputBytes);
      return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant()[..8];
    }
#pragma warning restore CA1850
  }

  /// <summary>
  /// Creates a unique instance ID for a linked model instance.
  /// </summary>
  /// <param name="definitionId">The definition ID of the linked model</param>
  /// <param name="instanceIndex">The index of this instance (1-based)</param>
  /// <returns>A unique instance ID</returns>
  public static string GenerateInstanceId(string definitionId, int instanceIndex) =>
    $"{definitionId}_instance_{instanceIndex}";
}
