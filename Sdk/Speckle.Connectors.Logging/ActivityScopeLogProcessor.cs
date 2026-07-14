using OpenTelemetry;
using OpenTelemetry.Logs;

namespace Speckle.Connectors.Logging;

/// <summary>
/// Adds <see cref="ActivityScope"/> tags to <see cref="LogRecord"/>s
/// </summary>
internal sealed class ActivityScopeLogProcessor : BaseProcessor<LogRecord>
{
  public override void OnEnd(LogRecord data)
  {
    if (ActivityScope.Tags.Count > 0)
    {
      if (data.Attributes is null)
      {
        data.Attributes = ActivityScope.TagsList;
      }
      else if (data.Attributes.Count > 0)
      {
        data.Attributes = data.Attributes.Concat(ActivityScope.Tags).ToList();
      }
    }
  }
}
