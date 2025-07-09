namespace Speckle.Connectors.GrasshopperShared.Components;

// NOTE: The number of spaces determines the order in which they display in the ribbon (nice hack)
public static class ComponentCategories
{
  // ribbon
  public const string PRIMARY_RIBBON = "Speckle";

  // categories
  public const string OPERATIONS = "    Models";
  public const string OBJECTS = "  Objects";
  public const string COLLECTIONS = "   Collections";
  public const string PARAMETERS = " Params";
  public const string DEVELOPER = "Dev";
}

public enum ComponentState
{
  Expired,
  NeedsInput,
  Receiving,
  Ready,
  Sending,
  UpToDate
}
