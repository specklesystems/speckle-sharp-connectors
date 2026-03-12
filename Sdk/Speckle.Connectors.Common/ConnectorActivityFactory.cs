using Speckle.Connectors.Logging;
using Speckle.Sdk.Logging;

namespace Speckle.Connectors.Common;

public sealed class ConnectorActivityFactory : ISdkActivityFactory
{
  private readonly LoggingActivityFactory _loggingActivityFactory = new();

  public void SetTag(string key, object? value) => _loggingActivityFactory.SetTag(key, value);

  public void Dispose() => _loggingActivityFactory.Dispose();

  public ISdkActivity? Start(string? name = default, string source = "")
  {
    LoggingActivity? activity = _loggingActivityFactory.Start(name ?? source);
    if (activity is null)
    {
      return null;
    }

    return new ConnectorActivity(activity.Value);
  }

  public ISdkActivity? StartRemote(string traceId, string parentSpanId, string? name = default, string source = "")
  {
    LoggingActivity? activity = _loggingActivityFactory.StartRemote(name ?? source, traceId, parentSpanId);
    if (activity is null)
    {
      return null;
    }
    return new ConnectorActivity(activity.Value);
  }

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
          _ => throw new ArgumentOutOfRangeException(nameof(code), code, null)
        }
      );

    public void InjectHeaders(Action<string, string> header) => activity.InjectHeaders(header);
  }
}
