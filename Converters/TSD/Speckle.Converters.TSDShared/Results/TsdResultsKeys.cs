namespace Speckle.Converters.TSDShared.Results;

public static class TsdResultsKeys
{
  public const string ELEMENT_END_FORCES = "Element End Forces";
  public const string OFFSET_FORCES = "Offset Forces";
  public const string REACTIONS = "Reactions";
  public const string SLAB_FORCES = "Slab Forces";
  public const string WALL_FORCES = "Wall Forces";
  public const string NODAL_DISPLACEMENTS = "Nodal Displacements";
  public const string OFFSET_DISPLACEMENTS = "Offset Displacements";
  public const string RESULT_LINE_FORCES = "Result Line Forces";
  public const string WALL_LINE_FORCES = "Wall Line Forces";

  public static readonly string[] All =
  [
    ELEMENT_END_FORCES,
    OFFSET_FORCES,
    REACTIONS,
    SLAB_FORCES,
    WALL_FORCES,
    NODAL_DISPLACEMENTS,
    OFFSET_DISPLACEMENTS,
    RESULT_LINE_FORCES,
    WALL_LINE_FORCES,
  ];
}
