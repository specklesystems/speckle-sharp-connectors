using System.Diagnostics;
using System.Reflection;

namespace Speckle.Connectors.Logging;

public sealed class LoggingActivityFactory : IDisposable
{
  private readonly ActivitySource _activitySource = new(
    Consts.TRACING_SOURCE,
    Consts.GetPackageVersion(Assembly.GetExecutingAssembly())
  );

  private readonly Dictionary<string, object?> _tags = new();

  public void SetTag(string key, object? value) => _tags[key] = value;

  public LoggingActivity? StartRemote(string name, string traceContext, LoggingActivityKind activityKind)
  {
    if (!ActivityContext.TryParse(traceContext, null, true, out ActivityContext context))
    {
      throw new ArgumentException("traceContext was not parsable to a valid W3C Header", nameof(traceContext));
    }

    //If you get a MissingManifestResourceException, Likely source or name is empty string, which is no good.
    var activity = _activitySource.StartActivity(name, ToOtelType(activityKind), context, _tags);
    if (activity is null)
    {
      return null;
    }
    return new LoggingActivity(activity);
  }

  public LoggingActivity? Start(string name, LoggingActivityKind activityKind)
  {
    //If you get a MissingManifestResourceException, Likely source or name is empty string, which is no good.
    var activity = _activitySource.StartActivity(ToOtelType(activityKind), tags: _tags, name: name);
    if (activity is null)
    {
      return null;
    }
    return new LoggingActivity(activity);
  }

  public void Dispose() => _activitySource.Dispose();

  private static ActivityKind ToOtelType(LoggingActivityKind kind) =>
    kind switch
    {
      LoggingActivityKind.Internal => ActivityKind.Internal,
      LoggingActivityKind.Server => ActivityKind.Server,
      LoggingActivityKind.Client => ActivityKind.Client,
      LoggingActivityKind.Producer => ActivityKind.Producer,
      LoggingActivityKind.Consumer => ActivityKind.Consumer,
      _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}
