namespace Speckle.Converters.CSiShared.Utils;

// NOTE: Space for string consts used across multiple files and in multiple contexts
// Separate onto dedicated files if this gets too long

/// <summary>
/// These categories mirror the UI. Nested within the properties are the repeated categories
/// This happens across frame, shell and joint objects.
/// The consts formalise the assignable property categories and reduce typos.
/// </summary>
public static class ObjectPropertyCategory
{
  public const string ASSIGNMENTS = "Assignments";
  public const string DESIGN = "Design";
  public const string GEOMETRY = "Geometry";
  public const string OBJECT_ID = "Object ID";
}

/// <summary>
/// These strings are repeatedly used as keys when building the properties dictionary for objects
/// </summary>
public static class ObjectPropertyKey
{
  public const string AREA = "Area";
  public const string CROSS_SECTIONAL_AREA = "Cross-Sectional Area";
  public const string DESIGN_PROCEDURE = "Design Procedure";
  public const string LENGTH = "Length";
  public const string MATERIAL_ID = "Material";
  public const string SECTION_ID = "Section Property";
  public const string THICKNESS = "Thickness";
  public const string VOLUME = "Volume";
}

/// <summary>
/// These strings are repeatedly used group properties (mimics the host app UI)
/// </summary>
public static class SectionPropertyCategory
{
  public const string DESIGN_DATA = "Design Data";
  public const string GENERAL_DATA = "General Data";
  public const string MECHANICAL_DATA = "Mechanical Data";
  public const string MODIFIERS = "Modifiers";
  public const string PROPERTY_DATA = "Property Data";
  public const string SECTION_PROPERTIES = "Section Properties";
  public const string SECTION_DIMENSIONS = "Section Dimensions";
  public const string WEIGHT_AND_MASS = "Weight and Mass";
}

/// <summary>
/// These strings are properties repeated and common to various object types (joint, frame, shell etc.)
/// </summary>
public static class CommonObjectProperty
{
  public const string LABEL = "Label";
  public const string LEVEL = "Level";
  public const string GROUPS = "Groups";
  public const string SPRING_ASSIGNMENT = "Spring Assignment";
  public const string LOCAL_AXIS_2_ANGLE = "Local Axis 2 Angle";
  public const string MATERIAL_OVERWRITE = "Material Overwrite";
  public const string PROPERTY_MODIFIERS = "Property Modifiers";
  public const string ANGLE = "Angle";
  public const string ADVANCED = "Advanced";
  public const string DESIGN_ORIENTATION = "Design Orientation";
}

/// <summary>
/// These strings are repeated when defining UI dropdown list `ResultTypeSetting.cs` as well as `CsiResultsExtractorFactory.cs`/>
/// </summary>
public static class ResultsKey
{
  public const string BASE_REACT = "Base Reactions";
  public const string DIAPHRAGM_CENTER_OF_MASS_DISPLACEMENTS = "Diaphragm Center Of Mass Displacements";
  public const string FRAME_FORCES = "Frame Forces";
  public const string JOINT_REACT = "Joint Reactions";
  public const string MODAL_PERIOD = "Modal Period";
  public const string PIER_FORCES = "Pier Forces";
  public const string SPANDREL_FORCES = "Spandrel Forces";
  public const string STORY_DRIFTS = "Story Drifts";
  public const string STORY_FORCES = "Story Forces";

  // Used by ResultTypeSetting to get all defined result keys
  public static readonly string[] All =
  [
    BASE_REACT,
    DIAPHRAGM_CENTER_OF_MASS_DISPLACEMENTS,
    FRAME_FORCES,
    JOINT_REACT,
    MODAL_PERIOD,
    PIER_FORCES,
    SPANDREL_FORCES,
    STORY_DRIFTS,
    STORY_FORCES
  ];
}
