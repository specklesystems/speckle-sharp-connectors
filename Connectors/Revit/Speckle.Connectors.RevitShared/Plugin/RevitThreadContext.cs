using Revit.Async;
using Speckle.Connectors.Common.Threading;

namespace Speckle.Connectors.Revit.Plugin;

public class RevitThreadContext : ThreadContext
{
  protected override Task<T> MainToWorkerAsync<T>(Func<Task<T>> action) => action();

  protected override Task<T> WorkerToMainAsync<T>(Func<Task<T>> action) =>
    RevitTask.RunAsync(async () => await action());

  protected override Task<T> MainToWorker<T>(Func<T> action) => Task.FromResult(action());

  protected override Task<T> WorkerToMain<T>(Func<T> action) => RevitTask.RunAsync(action);

  protected override Task RunMainAsync(Func<Task> action) =>  RevitTask.RunAsync(action);

  protected override Task<T> RunMainAsync<T>(Func<T> action) =>  RevitTask.RunAsync(action);

  protected override Task<T> RunMainAsync<T>(Func<Task<T>> action) => RevitTask.RunAsync(action);
}
