namespace Speckle.Connectors.Rhino.Mapper.Revit;

/// <summary>
/// Represents layers that are all assigned to the same category.
/// </summary>
public record LayerCategoryMapping(
  string CategoryValue,
  string CategoryLabel,
  IReadOnlyList<string> LayerIds,
  IReadOnlyList<string> LayerNames,
  int LayerCount
);
