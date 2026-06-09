using System.Diagnostics;

namespace Speckle.Connectors.Logging;

/// <summary>
/// Provides a wrapper around <see cref="Activity"/>.
/// Since <see cref="Activity"/> is ILRepacked and thus the type is not not accessible by consumers
/// </summary>
/// <remarks>
/// The reason we're ilrepacking is to avoid .NET framework connectors from
/// having a dependency on <see cref="System.Diagnostics.DiagnosticSource"/>
/// To avoid any potential DLL conflicts.
/// However, I recall this was done to "play safe" rather than avoid any known conflicts.
/// </remarks>
public readonly struct LoggingActivity
{
  private readonly Activity _activity;

  internal LoggingActivity(Activity activity)
  {
    _activity = activity;
  }

  public void Dispose() => _activity.Dispose();

  public void SetTag(string key, object? value) => _activity.SetTag(key, value);

  public void SetBaggage(string key, string? value) => _activity.SetBaggage(key, value);

  public void RecordException(Exception e) => _activity.AddException(e);

  /// <summary>W3C <c>tracestate</c> header</summary>
  public string? TraceState => _activity.TraceStateString;

  /// <summary>W3C <c>traceparent</c> header</summary>
  public string TraceParent => $"00-{TraceId}-{SpanId}-{checked((byte)_activity.ActivityTraceFlags):X2}";

  public string TraceId => _activity.TraceId.ToString();
  public string SpanId => _activity.SpanId.ToString();

  public void SetStatus(LoggingActivityStatusCode code) =>
    //We need to do this gymnastics due to ILRepack
    _activity.SetStatus(
      code switch
      {
        LoggingActivityStatusCode.Error => ActivityStatusCode.Error,
        LoggingActivityStatusCode.Unset => ActivityStatusCode.Unset,
        LoggingActivityStatusCode.Ok => ActivityStatusCode.Ok,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, null),
      }
    );

  /// <summary>
  /// <paramref name="headerHandler"/> will be called to handle each trace values
  /// stored in the <see cref="Activity"/> object
  /// so it can be injected into the headers of an HTTP request.
  /// </summary>
  public void InjectHeaders(Action<string, string> headerHandler) =>
    DistributedContextPropagator.Current.Inject(
      _activity,
      headerHandler,
      static (carrier, key, value) =>
      {
        if (carrier is Action<string, string> request)
        {
          request.Invoke(key, value);
        }
      }
    );
}
