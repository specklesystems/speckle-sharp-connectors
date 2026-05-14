namespace Speckle.Connectors.DUI.Bindings;

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

public readonly struct UpdateResult
{
  public bool IsSuccess { get; }
  public string? ErrorMessage { get; }

  private UpdateResult(bool success, string? error)
  {
    IsSuccess = success;
    ErrorMessage = error;
  }

  public static UpdateResult Success() => new(true, null);

  public static UpdateResult Fail(string message) => new(false, message);
}
