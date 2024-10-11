using System.Diagnostics;
using OpenTelemetry;

namespace Speckle.Connectors.Logging;

internal sealed class ActivityScopeProcessor : BaseProcessor<Activity>
{
  public override void OnEnd(Activity data)
  {
    foreach (KeyValuePair<string, object> keyValuePair in ActivityScope.Tags)
    {
      data.SetTag(keyValuePair.Key, keyValuePair.Value);
    }
  }
}
