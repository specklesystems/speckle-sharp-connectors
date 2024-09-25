using Speckle.Connectors.Logging;
using Speckle.Sdk.Common;
using Speckle.Sdk.Logging;

namespace Speckle.Connectors.Common;

public sealed class ConnectorActivityFactory : ISdkActivityFactory, IDisposable
{
  private readonly LoggingActivityFactory _loggingActivityFactory = new();

  public void Dispose() => _loggingActivityFactory.Dispose();

  public ISdkActivity? Start(string? name = default, string source = "")
  {
    var activity = _loggingActivityFactory?.Start(name, source);
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
          _ => throw new ArgumentOutOfRangeException(nameof(code), code, null)
        }
      );
  }
}
