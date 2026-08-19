using System.Diagnostics;
using OpenTelemetry;

namespace Speckle.Connectors.Logging;

/// <summary>
/// Adds <see cref="ActivityScope"/> tags to <see cref="Activity"/>
/// </summary>
internal sealed class ActivityScopeActivityProcessor : BaseProcessor<Activity>
{
  public override void OnEnd(Activity data)
  {
    foreach (KeyValuePair<string, object?> keyValuePair in ActivityScope.Tags)
    {
      data.SetTag(keyValuePair.Key, keyValuePair.Value);
    }
  }
}
