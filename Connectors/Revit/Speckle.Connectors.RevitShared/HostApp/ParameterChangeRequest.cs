namespace Speckle.Connectors.Revit.HostApp;

public class ParameterChangeRequest
{
  public required string ApplicationId { get; init; }
  public required string Path { get; init; }
  public object? To { get; init; }
}
