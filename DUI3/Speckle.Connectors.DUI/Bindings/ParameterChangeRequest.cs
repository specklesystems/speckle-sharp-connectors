namespace Speckle.Connectors.DUI.Bindings;

public class ParameterChangeRequest
{
  public required string ApplicationId { get; init; }
  public required string Path { get; init; }
  public object? To { get; init; }
  public string? InternalDefinitionName { get; set; }
  /// <summary>
  /// When true, the widget is requesting a new parameter be created rather than an existing one updated.
  /// Host apps that support open key-value storage (Rhino) will create the key automatically.
  /// Schema-bound apps (Revit, Civil3D) will return a descriptive error.
  /// </summary>
  public bool IsCreation { get; init; }
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
