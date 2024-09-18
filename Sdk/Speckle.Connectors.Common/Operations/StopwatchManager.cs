using System.Diagnostics;
using Speckle.InterfaceGenerator;

namespace Speckle.Connectors.Common.Operations;

[GenerateAutoInterface]
public class StopwatchManager : IStopwatchManager
{
  private readonly Stopwatch _stopwatch = new();

  public void Start() => _stopwatch.Start();

  public double ElapsedSeconds => _stopwatch.Elapsed.TotalSeconds;

  public long ElapsedMilliseconds => _stopwatch.ElapsedMilliseconds;
}
