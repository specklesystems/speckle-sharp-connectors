using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Operations;

namespace Speckle.Importers.Rhino;

public class Progress(ILogger<Progress> logger) : IProgress<CardProgress>
{
  private readonly TimeSpan _debounce = TimeSpan.FromMilliseconds(1000);
  private DateTime _lastTime = DateTime.UtcNow;

  public void Report(CardProgress value)
  {
    var now = DateTime.UtcNow;
    if (now - _lastTime >= _debounce)
    {
      logger.LogInformation(value.Status + " p " + value.Progress);
      _lastTime = now;
    }
  }
}
