namespace Speckle.Connectors.GrasshopperShared.Components;

// NOTE: The number of spaces determines the order in which they display in the ribbon (nice hack)
public static class ComponentCategories
{
  public const string PRIMARY_RIBBON = "Speckle";
  public const string OPERATIONS = "1-Ops";
  public const string OBJECTS = "2-Objects";
  public const string COLLECTIONS = "3-Collections";
  public const string PARAMETERS = "4-Parameters";
  public const string DEVELOPER = "5-Dev";
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
