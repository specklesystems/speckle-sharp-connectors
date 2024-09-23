using System.Diagnostics;
using OpenTelemetry.Trace;

namespace Speckle.Connectors.Logging;

public readonly struct LoggingActivity
{
  private readonly Activity _activity;

  internal LoggingActivity(Activity activity)
  {
    _activity = activity;
  }

  public void Dispose() => _activity.Dispose();

  public void SetTag(string key, object? value) => _activity.SetTag(key, value);

  public void RecordException(Exception e) => _activity.RecordException(e);

  public string TraceId => _activity.TraceId.ToString();

  public void SetStatus(LoggingActivityStatusCode code) =>
    _activity.SetStatus(
      code switch
      {
        LoggingActivityStatusCode.Error => ActivityStatusCode.Error,
        LoggingActivityStatusCode.Unset => ActivityStatusCode.Unset,
        LoggingActivityStatusCode.Ok => ActivityStatusCode.Ok,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, null)
      }
    );
}
