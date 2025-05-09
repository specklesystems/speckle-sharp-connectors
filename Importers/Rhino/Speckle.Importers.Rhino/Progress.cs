using Speckle.Connectors.Common.Operations;

namespace Speckle.Importers.Rhino;

public class Progress : IProgress<CardProgress>
{
  private readonly TimeSpan _debounce = TimeSpan.FromMilliseconds(1000);
  private DateTime _lastTime = DateTime.UtcNow;

  public void Report(CardProgress value)
  {
    var now = DateTime.UtcNow;
    if (now - _lastTime >= _debounce)
    {
      Console.WriteLine(value.Status + " p " + value.Progress);

      _lastTime = now;
    }
  }
}
