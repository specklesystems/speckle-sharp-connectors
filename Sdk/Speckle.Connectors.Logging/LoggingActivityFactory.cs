using System.Diagnostics;
using System.Reflection;

namespace Speckle.Connectors.Logging;

public sealed class LoggingActivityFactory : IDisposable
{
  private readonly ActivitySource _activitySource =
    new(Consts.TRACING_SOURCE, Consts.GetPackageVersion(Assembly.GetExecutingAssembly()));

  private readonly Dictionary<string, object?> _tags = new();

  public void SetTag(string key, object? value) => _tags[key] = value;

  public LoggingActivity? StartRemote(string name, string traceId, string parentSpanId)
  {
    //If you get a MissingManifestResourceException, Likely source or name is empty string, which is no good.
    var activity = _activitySource.CreateActivity(
      name: name,
      parentContext: new ActivityContext(
        ActivityTraceId.CreateFromString(traceId.AsSpan()),
        ActivitySpanId.CreateFromString(parentSpanId.AsSpan()),
        ActivityTraceFlags.None,
        isRemote: true
      ),
      kind: ActivityKind.Client,
      tags: _tags
    );
    if (activity is null)
    {
      return null;
    }
    return new LoggingActivity(activity);
  }

  public LoggingActivity? Start(string name)
  {
    //If you get a MissingManifestResourceException, Likely source or name is empty string, which is no good.
    var activity = _activitySource.StartActivity(name: name, kind: ActivityKind.Client, tags: _tags);
    if (activity is null)
    {
      return null;
    }
    return new LoggingActivity(activity);
  }

  public void Dispose() => _activitySource.Dispose();
}
