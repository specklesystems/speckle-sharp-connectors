using System.Diagnostics;

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

  public void RecordException(Exception e) => _activity.AddException(e);

  public string TraceId => _activity.TraceId.ToString();

  public void SetStatus(LoggingActivityStatusCode code) =>
    _activity.SetStatus(
      code switch
      {
        LoggingActivityStatusCode.Error => ActivityStatusCode.Error,
        LoggingActivityStatusCode.Unset => ActivityStatusCode.Unset,
        LoggingActivityStatusCode.Ok => ActivityStatusCode.Ok,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, null),
      }
    );

  public void InjectHeaders(Action<string, string> header) =>
    DistributedContextPropagator.Current.Inject(
      _activity,
      header,
      static (carrier, key, value) =>
      {
        if (carrier is Action<string, string> request)
        {
          request.Invoke(key, value);
        }
      }
    );
}
