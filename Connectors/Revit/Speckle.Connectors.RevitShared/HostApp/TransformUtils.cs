using System.IO;
using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.DB;
using Speckle.DoubleNumerics;

namespace Speckle.Connectors.Revit.HostApp;

public static class TransformUtils
{
  /// <summary>
  /// Converts a Revit Transform to a Speckle Matrix4x4.
  /// Uses column-major 4x4 matrix representation to match Speckle conventions.
  /// </summary>
  /// <param name="transform">The Revit transform to convert</param>
  /// <returns>A Matrix4x4 representing the same transformation</returns>
  public static Matrix4x4 ToMatrix4x4(Transform transform) =>
    new(
      (float)transform.BasisX.X,
      (float)transform.BasisX.Y,
      (float)transform.BasisX.Z,
      0,
      (float)transform.BasisY.X,
      (float)transform.BasisY.Y,
      (float)transform.BasisY.Z,
      0,
      (float)transform.BasisZ.X,
      (float)transform.BasisZ.Y,
      (float)transform.BasisZ.Z,
      0,
      (float)transform.Origin.X,
      (float)transform.Origin.Y,
      (float)transform.Origin.Z,
      1
    );

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

  /// <summary>
  /// Checks if two transforms are approximately equal within a tolerance.
  /// Useful for comparing transforms that might have minor floating-point differences.
  /// </summary>
  /// <param name="transform1">First transform to compare</param>
  /// <param name="transform2">Second transform to compare</param>
  /// <param name="tolerance">Tolerance for comparison (default: 1e-6)</param>
  /// <returns>True if transforms are approximately equal</returns>
  public static bool AreTransformsEqual(Transform transform1, Transform transform2, double tolerance = 1e-6) =>
    AreXYZEqual(transform1.Origin, transform2.Origin, tolerance)
    && AreXYZEqual(transform1.BasisX, transform2.BasisX, tolerance)
    && AreXYZEqual(transform1.BasisY, transform2.BasisY, tolerance)
    && AreXYZEqual(transform1.BasisZ, transform2.BasisZ, tolerance);

  /// <summary>
  /// Checks if two XYZ points/vectors are approximately equal within a tolerance.
  /// </summary>
  private static bool AreXYZEqual(XYZ xyz1, XYZ xyz2, double tolerance) =>
    Math.Abs(xyz1.X - xyz2.X) < tolerance
    && Math.Abs(xyz1.Y - xyz2.Y) < tolerance
    && Math.Abs(xyz1.Z - xyz2.Z) < tolerance;
}
