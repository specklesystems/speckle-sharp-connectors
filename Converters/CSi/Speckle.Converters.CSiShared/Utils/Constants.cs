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
  public const string MATERIAL_ID = "Material";
  public const string SECTION_ID = "Section Property";
}

/// <summary>
/// These strings are repeatedly used group properties (mimics the host app UI)
/// </summary>
public static class SectionPropertyCategory
{
  public const string GENERAL_DATA = "General Data";
  public const string SECTION_PROPERTIES = "Section Properties";
  public const string SECTION_DIMENSIONS = "Section Dimensions";
  public const string PROPERTY_DATA = "Property Data";
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
