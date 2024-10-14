using OpenTelemetry;
using OpenTelemetry.Logs;

namespace Speckle.Connectors.Logging;

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
