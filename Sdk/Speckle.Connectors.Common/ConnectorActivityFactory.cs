using Speckle.Connectors.Logging;
using Speckle.Sdk.Logging;

namespace Speckle.Connectors.Common;

/// <summary>
/// Provides <see cref="Speckle.Sdk"/> with an implementation of <see cref="ISdkActivityFactory"/>
/// that wraps <see cref="ISdkActivity"/> with an underlying <see cref="LoggingActivity"/>
/// </summary>
public sealed class ConnectorActivityFactory : ISdkActivityFactory
{
  private readonly LoggingActivityFactory _loggingActivityFactory = new();

  public void Dispose() => _loggingActivityFactory.Dispose();

  public ISdkActivity? Start(
    string? name = null,
    SdkActivityKind kind = SdkActivityKind.Internal,
    IReadOnlyDictionary<string, object?>? tags = null,
    DateTimeOffset startTime = default,
    string source = ""
  )
  {
    LoggingActivity? activity = _loggingActivityFactory.Start(name ?? source, ToLoggingType(kind), tags, startTime);
    if (activity is null)
    {
      return null;
    }

    return new ConnectorActivity(activity.Value);
  }

  public ISdkActivity? StartRemote(
    string? traceParent,
    string? traceState,
    SdkActivityKind kind,
    string? name = null,
    IReadOnlyDictionary<string, object?>? tags = null,
    DateTimeOffset startTime = default,
    string source = ""
  )
  {
    LoggingActivity? activity = _loggingActivityFactory.StartRemote(
      name ?? source,
      traceParent,
      traceState,
      ToLoggingType(kind),
      tags,
      startTime
    );
    if (activity is null)
    {
      return null;
    }
    return new ConnectorActivity(activity.Value);
  }

  private static LoggingActivityKind ToLoggingType(SdkActivityKind kind) =>
    kind switch
    {
      SdkActivityKind.Internal => LoggingActivityKind.Internal,
      SdkActivityKind.Server => LoggingActivityKind.Server,
      SdkActivityKind.Client => LoggingActivityKind.Client,
      SdkActivityKind.Producer => LoggingActivityKind.Producer,
      SdkActivityKind.Consumer => LoggingActivityKind.Consumer,
      _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

  private readonly struct ConnectorActivity(LoggingActivity activity) : ISdkActivity
  {
    public void Dispose() => activity.Dispose();

    public void SetTag(string key, object? value) => activity.SetTag(key, value);

    public void RecordException(Exception e) => activity.RecordException(e);

    /// <inheritdoc />
    public string TraceParent => activity.TraceParent;

    /// <inheritdoc />
    public string? TraceState => activity.TraceState;
    public string TraceId => activity.TraceId;
    public string SpanId => activity.SpanId;

    public void SetStatus(SdkActivityStatusCode code) =>
      activity.SetStatus(
        code switch
        {
          SdkActivityStatusCode.Error => LoggingActivityStatusCode.Error,
          SdkActivityStatusCode.Unset => LoggingActivityStatusCode.Unset,
          SdkActivityStatusCode.Ok => LoggingActivityStatusCode.Ok,
          _ => throw new ArgumentOutOfRangeException(nameof(code), code, null),
        }
      );

    public void InjectHeaders(Action<string, string> header) => activity.InjectHeaders(header);
  }
}
