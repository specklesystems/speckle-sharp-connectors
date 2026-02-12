using Speckle.DoubleNumerics;

namespace Speckle.Converters.Autocad.Helpers;

/// <summary>
/// Helper class for working with transforms
/// </summary>
public static class TransformHelper
{
  /// <summary>
  /// Converts an AutoCAD matrix3d to a row-dominant Speckle Matrix4x4
  /// </summary>
  /// <param name="m"></param>
  /// <returns></returns>
  /// <remarks>
  /// Use for System Numerics operations, eg matrix and vector multiplication
  /// </remarks>
  public static Matrix4x4 ConvertToMatrix4x4(AG.Matrix3d m) =>
    new(
      m[0, 0],
      m[1, 0],
      m[2, 0],
      m[3, 0],
      m[0, 1],
      m[1, 1],
      m[2, 1],
      m[3, 1],
      m[0, 2],
      m[1, 2],
      m[2, 2],
      m[3, 2],
      m[0, 3],
      m[1, 3],
      m[2, 3],
      m[3, 3]
    );

  /// <summary>
  /// Speckle Instances use a transform that is column-dominant, not row dominant.
  /// </summary>
  /// <param name="m"></param>
  /// <returns></returns>
  /// <remarks>Use only for Speckle Instance object transforms.</remarks>
  public static Matrix4x4 ConvertToInstanceMatrix4x4(AG.Matrix3d m) =>
    new(
      m[0, 0],
      m[0, 1],
      m[0, 2],
      m[0, 3],
      m[1, 0],
      m[1, 1],
      m[1, 2],
      m[1, 3],
      m[2, 0],
      m[2, 1],
      m[2, 2],
      m[2, 3],
      m[3, 0],
      m[3, 1],
      m[3, 2],
      m[3, 3]
    );

  /// <summary>
  /// Get the transform matrix from an entity's OCS to the WCS
  /// </summary>
  /// <param name="normal"></param>
  /// <returns></returns>
  /// <remarks>
  /// Use this method for certain properties or methods on entities that return values in OCS
  /// </remarks>
  public static AG.Matrix3d GetTransformFromOCSToWCS(AG.Vector3d normal) => AG.Matrix3d.WorldToPlane(normal);
}
