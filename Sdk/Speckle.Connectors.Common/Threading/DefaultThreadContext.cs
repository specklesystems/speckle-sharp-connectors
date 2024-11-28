using System.Diagnostics.CodeAnalysis;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.Common.Threading;

public class DefaultThreadContext : ThreadContext
{
  private readonly SynchronizationContext _threadContext = SynchronizationContext.Current.NotNull("No UI thread to capture?");
  
  [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "TaskCompletionSource")]
  protected override Task<T> WorkerToMainAsync<T>(Func<Task<T>> action)
  {
    
    TaskCompletionSource<T> tcs = new();
    _threadContext.Post(
      async _ =>
      {
        try
        {
          T result = await RunContext(action).ConfigureAwait(false);
          tcs.SetResult(result);
        }
        catch (Exception ex)
        {
          tcs.SetException(ex);
        }
      },
      null
    );
    return tcs.Task;
  }
  
  protected override Task<T> MainToWorkerAsync<T>(Func<Task<T>> action)
  {
    Task<Task<T>> f = Task.Factory.StartNew(action, default, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    return f.Unwrap();
  }
  
  [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "TaskCompletionSource")]
  protected override Task<T> WorkerToMain<T>(Func<T> action)
  {
    TaskCompletionSource<T> tcs = new();
    _threadContext.Post(
      async _ =>
      {
        try
        {
          T result = await RunContext(action).ConfigureAwait(false);
          tcs.SetResult(result);
        }
        catch (Exception ex)
        {
          tcs.SetException(ex);
        }
      },
      null
    );
    return tcs.Task;
  }
  
  protected override Task<T> MainToWorker<T>(Func<T> action)
  {
    Task<T> f = Task.Factory.StartNew(action, default, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    return f;
  }
}
