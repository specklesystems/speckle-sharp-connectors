using Speckle.Converter.Navisworks.Geometry;
// ReSharper disable NotAccessedPositionalProperty.Global

namespace Speckle.Converter.Navisworks.Settings;

public record NavisworksConversionSettings(Derived Derived, User User);

public record Derived(
  NAV.Document Document,
  SafeBoundingBox ModelBoundingBox,
  SafeVector TransformVector,
  bool IsUpright,
  string SpeckleUnits
);

public record User(
  OriginMode OriginMode,
  bool IncludeInternalProperties,
  bool ConvertHiddenElements,
  RepresentationMode VisualRepresentationMode,
  bool CoalescePropertiesFromFirstObjectAncestor,
  bool ExcludeProperties,
  bool PreserveModelHierarchy,
  bool RevitCategoryMapping = true
);
