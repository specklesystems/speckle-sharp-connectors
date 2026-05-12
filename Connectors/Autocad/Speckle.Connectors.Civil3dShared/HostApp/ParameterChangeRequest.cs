namespace Speckle.Connectors.Civil3dShared.HostApp;

public class ParameterChangeRequest
{
  public required string ApplicationId { get; init; }
  public required string Path { get; init; }
  public object? To { get; init; }
  public string? InternalDefinitionName { get; set; }
}

public class ParameterChangesWrapper
{
  public List<ParameterChangeRequest>? Changes { get; set; }
}
