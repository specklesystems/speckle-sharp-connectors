using System.Runtime.CompilerServices;
using Speckle.Connectors.Logging;
using Speckle.Sdk.Logging;

namespace Speckle.Connectors.Common;

/// <summary>
/// Provides <see cref="Speckle.Sdk"/> with an implementation of <see cref="ISdkActivityFactory"/>
/// that wraps <see cref="ISdkActivity"/> with an underlying <see cref="LoggingActivity"/>.
/// </summary>
/// <remarks>
/// We have several layers of abstraction via interfaces and wrappers:<br/>
/// <c>System.Diagnostics.DiagnosticSource.Activity</c> is wrapped by
/// <see cref="LoggingActivity"/> is wrapped by
/// <see cref="ConnectorActivityFactory"/> implements the sdk abstraction <see cref="ISdkActivityFactory"/>.
/// <br/>
/// This is done so that the  .NET Standard 2.0 target of the <see cref="Speckle.Sdk"/> (and therefore consumers e.g. .NET framework connectors) can avoid a
/// dependency on <c>System.Diagnostics.DiagnosticSource</c>.
/// </remarks>
public sealed class ConnectorActivityFactory : ISdkActivityFactory
{
  private readonly LoggingActivityFactory _loggingActivityFactory = new();

  public void Dispose() => _loggingActivityFactory.Dispose();

  // NOTE: signatures updated to match the current Speckle.Sdk `ISdkActivityFactory` (the OpenTelemetry
  // remote-span refactor dropped the per-call `tags`/`startTime` args and collapsed remote context to a
  // single W3C `traceContext`). The underlying LoggingActivityFactory still accepts the richer args, so we
  // pass null/default for the dropped ones and treat `traceContext` as the W3C `traceparent`.
  public ISdkActivity? Start(
    string? name = null,
    SdkActivityKind kind = SdkActivityKind.Internal,
    [CallerMemberName] string source = ""
  )
  {
    LoggingActivity? activity = _loggingActivityFactory.Start(name ?? source, ToLoggingType(kind), null, default);
    if (activity is null)
    {
      return null;
    }

    return new ConnectorActivity(activity.Value);
  }

  public ISdkActivity? StartRemote(
    string traceContext,
    SdkActivityKind kind,
    string? name = null,
    [CallerMemberName] string source = ""
  )
  {
    LoggingActivity? activity = _loggingActivityFactory.StartRemote(
      name ?? source,
      traceContext,
      null,
      ToLoggingType(kind),
      null,
      default
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
