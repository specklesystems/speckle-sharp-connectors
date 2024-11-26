using Revit.Async;
using Speckle.Connectors.DUI.Threading;

namespace Speckle.Connectors.Revit.Plugin;

public class RevitThreadContext : ThreadContext
{
  public override Task RunContext(Func<Task> action) => RevitTask.RunAsync(action);

  public override void RunContext(Action action) => RevitTask.RunAsync(action);

  public override Task<T> RunContext<T>(Func<Task<T>> action) => RevitTask.RunAsync(action);
}
