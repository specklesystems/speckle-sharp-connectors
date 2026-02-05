using System.Runtime.CompilerServices;
using Speckle.Connectors.Logging;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Speckle.Sdk.Logging;

namespace Speckle.Connectors.Common;

public sealed class ConnectorActivityFactory(ISpeckleApplication application) : ISdkActivityFactory, IDisposable
{
  private readonly LoggingActivityFactory _loggingActivityFactory = new();

  public void SetTag(string key, object? value) => _loggingActivityFactory.SetTag(key, value);

  public void Dispose() => _loggingActivityFactory.Dispose();

  public ISdkActivity? Start(string? name = default, [CallerMemberName] string source = "")
  {
    var activity = _loggingActivityFactory.Start(application.ApplicationAndVersion + " " + (name ?? source));
    if (activity is null)
    {
      return null;
    }
    return new ConnectorActivity(activity.NotNull());
  }

  private readonly struct ConnectorActivity(LoggingActivity activity) : ISdkActivity
  {
    public void Dispose() => activity.Dispose();

    public void SetTag(string key, object? value) => activity.SetTag(key, value);

    public void RecordException(Exception e) => activity.RecordException(e);

    public string TraceId => activity.TraceId;

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
