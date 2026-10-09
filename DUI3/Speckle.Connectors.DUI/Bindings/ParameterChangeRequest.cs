namespace Speckle.Connectors.DUI.Bindings;

public class ParameterChangeRequest
{
  public required string ApplicationId { get; init; }
  public required string Path { get; init; }
  public object? To { get; init; }
  public string? InternalDefinitionName { get; set; }

  /// <summary>
  /// When true, the widget could not see a value for this parameter on the object. Host apps with open key-value
  /// storage (Rhino) set the key; schema-bound apps (Revit, Civil3D) update the parameter when the path resolves
  /// to one and create it otherwise.
  /// </summary>
  public bool IsCreation { get; init; }
}

public class ParameterChangesWrapper
{
  public List<ParameterChangeRequest>? Changes { get; set; }
}

/// <summary>
/// What the host app did with a change request payload; the DUI keeps the request open while <see cref="Failed"/>
/// is non-zero.
/// </summary>
public record ParameterUpdateSummary(int Applied, int Failed, IReadOnlyList<string> Errors);

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
