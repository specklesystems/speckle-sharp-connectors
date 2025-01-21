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
