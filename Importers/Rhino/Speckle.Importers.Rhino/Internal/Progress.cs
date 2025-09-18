using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Operations;

namespace Speckle.Importers.Rhino.Internal;

internal sealed class Progress(ILogger<Progress> logger) : IProgress<CardProgress>
{
  private readonly TimeSpan _debounce = TimeSpan.FromMilliseconds(1000);
  private DateTime _lastTime = DateTime.UtcNow;

  public void Report(CardProgress value)
  {
    var now = DateTime.UtcNow;
    if (now - _lastTime >= _debounce)
    {
      logger.LogInformation("{Status} p {Progress}", value.Status, value.Progress);
      _lastTime = now;
    }
  }
}
