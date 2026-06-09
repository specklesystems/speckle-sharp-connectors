using System.Diagnostics;
using System.Reflection;

namespace Speckle.Connectors.Logging;

public sealed class LoggingActivityFactory : IDisposable
{
  private readonly ActivitySource _activitySource = new(
    Consts.TRACING_SOURCE,
    Consts.GetPackageVersion(Assembly.GetExecutingAssembly())
  );

  public LoggingActivity? StartRemote(
    string name,
    string? traceParent,
    string? traceState,
    LoggingActivityKind kind,
    IReadOnlyDictionary<string, object?>? tags,
    DateTimeOffset startTime
  )
  {
    if (!ActivityContext.TryParse(traceParent, traceState, true, out ActivityContext context))
    {
      throw new ArgumentException(
        "traceContext was not parsable to a valid W3C traceParent or traceState Header",
        nameof(traceParent)
      );
    }

    //If you get a MissingManifestResourceException, Likely source or name is empty string, which is no good.
    var activity = _activitySource.StartActivity(name, ToOtelType(kind), context, tags, null, startTime);
    if (activity is null)
    {
      return null;
    }
    return new LoggingActivity(activity);
  }

  public LoggingActivity? Start(
    string name,
    LoggingActivityKind kind,
    IReadOnlyDictionary<string, object?>? tags,
    DateTimeOffset startTime
  )
  {
    //If you get a MissingManifestResourceException, Likely source or name is empty string, which is no good.
    var activity = _activitySource.StartActivity(name, ToOtelType(kind), null, tags, null, startTime);
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
