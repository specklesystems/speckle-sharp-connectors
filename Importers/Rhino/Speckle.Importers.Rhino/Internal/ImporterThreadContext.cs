using Speckle.Connectors.Common.Threading;

namespace Speckle.Importers.Rhino.Internal;

/// <summary>
/// A custom implementation of the ThreadContext to behave correctly with windowless rhino
/// </summary>
internal sealed class ImporterThreadContext : ThreadContext
{
  protected override Task<T> WorkerToMainAsync<T>(Func<Task<T>> action)
  {
    var t = Task.Factory.StartNew(action, default, TaskCreationOptions.AttachedToParent, TaskScheduler.Default);
    return t.Unwrap();
  }

  protected override Task<T> MainToWorkerAsync<T>(Func<Task<T>> action)
  {
    Task<Task<T>> f = Task.Factory.StartNew(
      action,
      default,
      TaskCreationOptions.AttachedToParent,
      TaskScheduler.Default
    );
    return f.Unwrap();
  }

  protected override Task<T> WorkerToMain<T>(Func<T> action)
  {
    var t = Task.Factory.StartNew(action, default, TaskCreationOptions.AttachedToParent, TaskScheduler.Default);
    return t;
  }

  protected override Task<T> MainToWorker<T>(Func<T> action)
  {
    Task<T> f = Task.Factory.StartNew(action, default, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    return f;
  }
}
