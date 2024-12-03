using Revit.Async;
using Speckle.Connectors.Common.Threading;

namespace Speckle.Connectors.Revit.Plugin;

public class RevitThreadContext : ThreadContext
{
  protected override ValueTask<T> MainToWorkerAsync<T>(Func<ValueTask<T>> action) => action();

  protected override ValueTask<T> WorkerToMainAsync<T>(Func<ValueTask<T>> action) => RevitTask.RunAsync(async () => await action().BackToCurrent()).AsValueTask();

  protected override ValueTask<T> MainToWorker<T>(Func<T> action) => new(action());

  protected override ValueTask<T> WorkerToMain<T>(Func<T> action) => new(RevitTask.RunAsync(action));
}
