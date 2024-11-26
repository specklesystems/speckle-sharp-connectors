using System.Diagnostics.CodeAnalysis;
using Speckle.InterfaceGenerator;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.DUI.Threading;

[GenerateAutoInterface]
public class MainThreadContext : IMainThreadContext
{
  private readonly SynchronizationContext _mainThreadContext;

  // Do this when you start your application
  private static int s_mainThreadId;

  public MainThreadContext()
  {
    _mainThreadContext = SynchronizationContext.Current.NotNull("No UI thread to capture?");
    s_mainThreadId = Environment.CurrentManagedThreadId;
  }

  public static bool IsMainThread => Environment.CurrentManagedThreadId == s_mainThreadId;

  public virtual void RunContext(Action action) => action();

  public void RunOnThread(Action action, bool useMain)
  {
    if (useMain)
    {
      if (IsMainThread)
      {
        RunContext(action);
      }
      else
      {
        _mainThreadContext.Post(
          _ =>
          {
            RunContext(action);
          },
          null
        );
      }
    }
    else
    {
      if (IsMainThread)
      {
        Task.Factory.StartNew(action, default, TaskCreationOptions.LongRunning, TaskScheduler.Default)
          .GetAwaiter()
          .GetResult();
      }
      else
      {
        RunContext(action);
      }
    }
  }

  public async Task RunOnThreadAsync(Func<Task> action, bool useMain)
  {
    if (useMain)
    {
      if (IsMainThread)
      {
        await action.Invoke().ConfigureAwait(false);
      }
      else
      {
        await RunOnThreadAsync<object?>(
            async () =>
            {
              await RunContext(action.Invoke).ConfigureAwait(false);
              return null;
            },
            useMain
          )
          .ConfigureAwait(false);
      }
    }
    else
    {
      if (IsMainThread)
      {
        await Task
          .Factory.StartNew(action, default, TaskCreationOptions.LongRunning, TaskScheduler.Default)
          .ConfigureAwait(false);
      }
      else
      {
        await RunContext(action.Invoke).ConfigureAwait(false);
      }
    }
  }

  public virtual Task RunContext(Func<Task> action) => action();

  public virtual Task<T> RunContext<T>(Func<Task<T>> action) => action();

  [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "TaskCompletionSource")]
  public Task<T> RunOnThreadAsync<T>(Func<Task<T>> action, bool useMain)
  {
    if (useMain)
    {
      if (IsMainThread)
      {
        return RunContext(action.Invoke);
      }
      TaskCompletionSource<T> tcs = new();
      _mainThreadContext.Post(
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
    if (IsMainThread)
    {
      Task<Task<T>> f = Task.Factory.StartNew(action, default, TaskCreationOptions.LongRunning, TaskScheduler.Default);
      return f.Unwrap();
    }
    return RunContext(action.Invoke);
  }
}
