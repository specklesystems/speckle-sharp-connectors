namespace Speckle.Connectors.GrasshopperShared.Components;

// NOTE: The number of spaces determines the order in which they display in the ribbon (nice hack)
public static class ComponentCategories
{
  public const string PRIMARY_RIBBON = "Speckle";
  public const string OPERATIONS = "    Operations";
  public const string COLLECTIONS = "   Collections";
  public const string OBJECTS = "  Objects";
  public const string PARAMETERS = "Parameters";
}

public enum ComponentState
{
  Cancelled,
  Expired,
  NeedsInput,
  Receiving,
  Ready,
  Sending,
  UpToDate
}
