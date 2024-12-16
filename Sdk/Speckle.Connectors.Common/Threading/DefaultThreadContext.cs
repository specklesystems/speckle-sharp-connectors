using System.Diagnostics.CodeAnalysis;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.Common.Threading;

public class DefaultThreadContext : ThreadContext
{
  private readonly SynchronizationContext _threadContext = SynchronizationContext.Current.NotNull(
    "No UI thread to capture?"
  );

  [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "TaskCompletionSource")]
  protected override ValueTask<T> WorkerToMainAsync<T>(Func<ValueTask<T>> action)
  {
    TaskCompletionSource<T> tcs = new();
    _threadContext.Post(
      async _ =>
      {
        try
        {
          T result = await action();
          tcs.SetResult(result);
        }
        catch (Exception ex)
        {
          tcs.SetException(ex);
        }
      },
      null
    );
    return new ValueTask<T>(tcs.Task);
  }

  protected override ValueTask<T> MainToWorkerAsync<T>(Func<ValueTask<T>> action)
  {
    Task<Task<T>> f = Task.Factory.StartNew(
      async () => await action(),
      default,
      TaskCreationOptions.LongRunning,
      TaskScheduler.Default
    );
    return new ValueTask<T>(f.Unwrap());
  }

  [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "TaskCompletionSource")]
  protected override ValueTask<T> WorkerToMain<T>(Func<T> action)
  {
    TaskCompletionSource<T> tcs = new();
    _threadContext.Post(
      _ =>
      {
        try
        {
          T result = action();
          tcs.SetResult(result);
        }
        catch (Exception ex)
        {
          tcs.SetException(ex);
        }
      },
      null
    );
    return new ValueTask<T>(tcs.Task);
  }

  protected override ValueTask<T> MainToWorker<T>(Func<T> action)
  {
    Task<T> f = Task.Factory.StartNew(action, default, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    return new ValueTask<T>(f);
  }
}
