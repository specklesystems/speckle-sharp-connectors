namespace Speckle.Connectors.Logging;

/// <summary>
/// Creates context that is scoped to an <see cref="AsyncLocal{T}"/> state machine.
/// The tags are added to both logs and traces via <see cref="ActivityScopeLogProcessor"/> and <see cref="ActivityScopeActivityProcessor"/>
/// </summary>
/// <remarks>
/// The only thing that makes this useful is it adds info to traces and logs
/// OTEL would traces already inherit scoped context without the help of this class...
/// </remarks>
public static class ActivityScope
{
  private static readonly AsyncLocal<Dictionary<string, object?>> s_tags = new() { Value = new() };
  public static IReadOnlyDictionary<string, object?> Tags => s_tags.Value ??= new();
  public static IReadOnlyList<KeyValuePair<string, object?>> TagsList { get; } =
    new List<KeyValuePair<string, object?>>(s_tags.Value ??= new());

  public static IDisposable SetTag(string key, string value)
  {
    s_tags.Value ??= new();
    s_tags.Value[key] = value;
    return new TagScope(key);
  }

  private sealed class TagScope(string key) : IDisposable
  {
    public void Dispose() => s_tags.Value?.Remove(key);
  }
}
