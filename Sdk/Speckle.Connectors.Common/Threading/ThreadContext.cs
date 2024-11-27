using System.Diagnostics.CodeAnalysis;
using Speckle.InterfaceGenerator;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.Common.Threading;

[GenerateAutoInterface]
public class ThreadContext : IThreadContext
{
  private readonly SynchronizationContext _threadContext;

  // Do this when you start your application
  private static int s_mainThreadId;

  public ThreadContext()
  {
    _threadContext = SynchronizationContext.Current.NotNull("No UI thread to capture?");
    s_mainThreadId = Environment.CurrentManagedThreadId;
  }

  public static bool IsMainThread => Environment.CurrentManagedThreadId == s_mainThreadId;

  protected virtual void RunContext(Action action) => action();

  protected virtual Task<T> RunContext<T>(Func<T> action) => Task.FromResult(action());

  protected virtual Task RunContext(Func<Task> action) => action();

  protected virtual Task<T> RunContext<T>(Func<Task<T>> action) => action();

  public virtual void RunOnThread(Action action, bool useMain)
  {
    if (useMain)
    {
      if (IsMainThread)
      {
        RunContext(action);
      }
      else
      {
        _threadContext.Post(
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

  [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "TaskCompletionSource")]
  public virtual Task<T> RunOnThread<T>(Func<T> action, bool useMain)
  {
    if (useMain)
    {
      if (IsMainThread)
      {
        return RunContext(action.Invoke);
      }
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
    if (IsMainThread)
    {
      Task<T> f = Task.Factory.StartNew(action, default, TaskCreationOptions.LongRunning, TaskScheduler.Default);
      return f;
    }

    return RunContext(action.Invoke);
  }

  public virtual async Task RunOnThreadAsync(Func<Task> action, bool useMain)
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

  [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "TaskCompletionSource")]
  public virtual Task<T> RunOnThreadAsync<T>(Func<Task<T>> action, bool useMain)
  {
    if (useMain)
    {
      if (IsMainThread)
      {
        return RunContext(action.Invoke);
      }
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
    if (IsMainThread)
    {
      Task<Task<T>> f = Task.Factory.StartNew(action, default, TaskCreationOptions.LongRunning, TaskScheduler.Default);
      return f.Unwrap();
    }
    return RunContext(action.Invoke);
  }
}
