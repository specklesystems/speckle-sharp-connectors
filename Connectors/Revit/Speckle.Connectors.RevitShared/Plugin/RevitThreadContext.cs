using Revit.Async;
using Speckle.Connectors.Common.Threading;

namespace Speckle.Connectors.Revit.Plugin;

public class RevitThreadContext : ThreadContext
{
  protected override Task<T> RunContext<T>(Func<T> action) => RevitTask.RunAsync(action);

  protected override Task RunContext(Func<Task> action) => RevitTask.RunAsync(action);

  protected override void RunContext(Action action) => RevitTask.RunAsync(action);

  protected override Task<T> RunContext<T>(Func<Task<T>> action) => RevitTask.RunAsync(action);
}
