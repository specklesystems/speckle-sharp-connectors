using Speckle.Connectors.Logging;
using Speckle.Sdk.Logging;

namespace Speckle.Connectors.Common;

public sealed class ConnectorActivityFactory : ISdkActivityFactory
{
  private readonly LoggingActivityFactory _loggingActivityFactory = new();

  public void SetTag(string key, object? value) => _loggingActivityFactory.SetTag(key, value);

  public void Dispose() => _loggingActivityFactory.Dispose();

  public ISdkActivity? Start(string? name, SdkActivityKind kind, string source)
  {
    LoggingActivity? activity = _loggingActivityFactory.Start(name ?? source, ToLoggingType(kind));
    if (activity is null)
    {
      return null;
    }

    return new ConnectorActivity(activity.Value);
  }

  /// <param name="traceContext">W3C trace context header</param>
  /// <param name="kind"></param>
  /// <param name="name"></param>
  /// <returns></returns>
  public ISdkActivity? StartRemote(string traceContext, SdkActivityKind kind, string? name, string source)
  {
    LoggingActivity? activity = _loggingActivityFactory.StartRemote(name ?? source, traceContext, ToLoggingType(kind));
    if (activity is null)
    {
      return null;
    }
    return new ConnectorActivity(activity.Value);
  }

  //We need to do this gymnastics due to ILRepack
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
