namespace Speckle.Converter.Navisworks.Settings;

/// <summary>
/// Represents the settings used for Navisworks conversions.
/// </summary>
public record NavisworksConversionSettings(
  NAV.Document Document, // The active Navisworks document to be processed.
  string SpeckleUnits, // Units used in Speckle for standardised measurements.
  OriginMode OriginMode, // Defines the base point for transformations.
  bool IncludeInternalProperties, // Whether to include internal Navisworks properties in the output.
  bool ConvertHiddenElements, // Whether to include hidden elements during the conversion process.
  RepresentationMode VisualRepresentationMode, // Specifies the visual representation mode.
  bool CoalescePropertiesFromFirstObjectAncestor, // Whether to merge properties from the first object ancestor.
  NAV.Vector3D TransformVector, // Transformation vector applied to the model.
  bool IsUpright, // Indicates if the model's orientation is upright relative to canonical up.
  NAV.BoundingBox3D ModelBoundingBox, // The bounding box of the model.
  bool ExcludeProperties // Whether to exclude properties from the output.
);
