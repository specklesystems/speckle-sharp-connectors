namespace Speckle.Connectors.Rhino.Mapper.Revit;

/// <summary>
/// Represents a group of objects that are all assigned to the same category.
/// </summary>
public record CategoryMapping(
  string CategoryValue,
  string CategoryLabel,
  IReadOnlyList<string> ObjectIds,
  int ObjectCount
);
