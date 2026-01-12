using Speckle.Converter.Navisworks.Geometry;

namespace Speckle.Converter.Navisworks.Settings;

/// <summary>
/// Represents the settings used for Navisworks conversions.
/// </summary>
public record NavisworksConversionSettings(Derived Derived, User User);

// Derived from Navisworks Application
public record Derived(
  NAV.Document Document, // The active Navisworks document to be processed.
  SafeBoundingBox ModelBoundingBox, // The bounding box of the model.
  SafeVector TransformVector, // Transformation vector applied to the model.
  bool IsUpright, // Indicates if the model's orientation is upright relative to canonical up.
  string SpeckleUnits // Units used in Speckle for standardized measurements.
);

// Optional settings for conversion to be offered in UI
public record User(
  OriginMode OriginMode, // Defines the base point for transformations.
  bool IncludeInternalProperties, // Whether to include internal Navisworks properties in the output.
  bool ConvertHiddenElements, // Whether to include hidden elements during the conversion process.
  RepresentationMode VisualRepresentationMode, // Specifies the visual representation mode.
  bool CoalescePropertiesFromFirstObjectAncestor, // Whether to merge properties from the first object ancestor.
  bool ExcludeProperties, // Whether to exclude properties from the output.
  bool PreserveModelHierarchy, // Whether to maintain the full model hierarchy during conversion.
  bool RevitCategoryMapping = true // Optional mapping to Revit categories (if applicable).
);
