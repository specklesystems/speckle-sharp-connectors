using Speckle.DoubleNumerics;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Common.ToSpeckle;

/// <summary>
/// Represents a display value extracted from a host app element, optionally with a transform matrix for instancing.
/// </summary>
/// <param name="Geometry">The extracted geometry as a Speckle Base object</param>
/// <param name="Transform">Optional transform matrix for instanced geometry. Null for non-instanced geometry.</param>
public readonly record struct DisplayValueResult(Base Geometry, Matrix4x4? Transform)
{
  /// <summary>
  /// Creates a display value result without a transform (non-instanced geometry).
  /// </summary>
  public static DisplayValueResult WithoutTransform(Base geometry) => new(geometry, null);

  /// <summary>
  /// Creates a display value result with a transform (instanced geometry).
  /// </summary>
  /// <remarks>
  /// Seems unnecessary, but reads nicely (self-documenting) in usage in my opinion (clear intent).
  /// </remarks>
  public static DisplayValueResult WithTransform(Base geometry, Matrix4x4 transform) => new(geometry, transform);
}
