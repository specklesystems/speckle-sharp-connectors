namespace Speckle.Connectors.Logging;

public static class ActivityScope
{
  private static readonly AsyncLocal<Dictionary<string, object>> s_tags = new() { Value = new() };

  public static IReadOnlyDictionary<string, object> Tags => s_tags.Value;

  public static IDisposable SetTag(string key, string value)
  {
    s_tags.Value[key] = value;
    return new TagScope(key);
  }

  private sealed class TagScope(string key) : IDisposable
  {
    public void Dispose() => s_tags.Value.Remove(key);
  }
}
