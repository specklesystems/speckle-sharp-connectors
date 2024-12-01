using System.Diagnostics.CodeAnalysis;

namespace Speckle.Converter.Navisworks.Settings;

/// <summary>
/// Defines the representation mode to be used during conversion.
/// </summary>
[SuppressMessage("ReSharper", "InconsistentNaming")]
public enum RepresentationMode
{
  /// <summary>
  /// Uses the current active representation (e.g., overrides or temporary settings).
  /// </summary>
  Active,

  /// <summary>
  /// Preserves the original representation as defined in the source data.
  /// </summary>
  Original,

  /// <summary>
  /// Applies a fixed or user-defined representation, overriding others.
  /// </summary>
  Permanent
}

/// <summary>
/// Specifies the origin mode, which defines the basis of a transformation
/// applied to the model before conversion. The transformation aligns
/// the model's origin point to a base point.
/// </summary>
[SuppressMessage("ReSharper", "InconsistentNaming")]
public enum OriginMode
{
  /// <summary>
  /// Uses the model origin as the base point. This is the default mode.
  /// No transformation is applied other than converting from the local
  /// coordinate system to the world coordinate system.
  /// </summary>
  ModelOrigin,

  /// <summary>
  /// Uses a user-defined project base point as the base point for the transformation.
  /// </summary>
  ProjectBasePoint,

  /// <summary>
  /// Uses the center of the model's bounding box as the base point for the transformation.
  /// </summary>
  BoundingBoxCenter
}
