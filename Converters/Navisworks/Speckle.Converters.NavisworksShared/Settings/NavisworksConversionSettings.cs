using Speckle.Converter.Navisworks.Geometry;

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
  bool RoundMeshVertexDoubles,
  bool ConvertHiddenElements,
  RepresentationMode VisualRepresentationMode,
  PropertyDetailLevel PropertyDetailLevel,
  GeometryDetailLevel GeometryDetailLevel,
  bool CoalescePropertiesFromFirstObjectAncestor,
  bool PreserveModelHierarchy,
  bool RevitCategoryMapping = true
);
