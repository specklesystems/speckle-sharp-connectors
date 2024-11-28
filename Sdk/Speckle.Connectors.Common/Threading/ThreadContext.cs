using Speckle.InterfaceGenerator;

namespace Speckle.Connectors.Common.Threading;

[GenerateAutoInterface]
public abstract class ThreadContext : IThreadContext
{
  private static readonly Task<object?> s_completedTask = Task.FromResult<object?>(null);

  public static bool IsMainThread => Environment.CurrentManagedThreadId == 1 && !Thread.CurrentThread.IsBackground;

  protected virtual void RunContext(Action action) => action();

  protected virtual Task<T> RunContext<T>(Func<T> action) => Task.FromResult(action());

  protected virtual Task RunContext(Func<Task> action) => action();

  protected virtual Task<T> RunContext<T>(Func<Task<T>> action) => action();

  public async Task RunOnThread(Action action, bool useMain)
  {
    if (useMain)
    {
      if (IsMainThread)
      {
        RunContext(action);
      }
      else
      {
        await WorkerToMainAsync(
            () =>
            {
              RunContext(action);
              return s_completedTask;
            }
          )
          .ConfigureAwait(false);
      }
    }
    else
    {
      if (IsMainThread)
      {
        await MainToWorkerAsync(() =>
        {
          action();
          return s_completedTask;
        }).BackToAny();
      }
      else
      {
        RunContext(action);
      }
    }
  }

  public virtual Task<T> RunOnThread<T>(Func<T> action, bool useMain)
  {
    if (useMain)
    {
      if (IsMainThread)
      {
        return RunContext(action);
      }

      return WorkerToMain(action);
    }
    if (IsMainThread)
    {
      return MainToWorker(action);
    }

    return RunContext(action);
  }

  public async Task RunOnThreadAsync(Func<Task> action, bool useMain)
  {
    if (useMain)
    {
      if (IsMainThread)
      {
        await RunContext(action).ConfigureAwait(false);
      }
      else
      {
        await WorkerToMainAsync<object?>(
            async () =>
            {
              await RunContext(action.Invoke).ConfigureAwait(false);
              return null;
            }
          )
          .ConfigureAwait(false);
      }
    }
    else
    {
      if (IsMainThread)
      {
        await MainToWorkerAsync<object?>(async () =>
        {
          await action().BackToAny();
          return null;
        }).BackToAny();
      }
      else
      {
        await RunContext(action.Invoke).ConfigureAwait(false);
      }
    }
  }

  public Task<T> RunOnThreadAsync<T>(Func<Task<T>> action, bool useMain)
  {
    if (useMain)
    {
      if (IsMainThread)
      {
        return RunContext(action.Invoke);
      }

      return WorkerToMainAsync(action);
    }
    if (IsMainThread)
    {
      return MainToWorkerAsync(action);
    }
    return RunContext(action.Invoke);
  }

  protected abstract Task<T> WorkerToMainAsync<T>(Func<Task<T>> action);

  protected abstract Task<T> MainToWorkerAsync<T>(Func<Task<T>> action);
  protected abstract Task<T> WorkerToMain<T>(Func<T> action);

  protected abstract Task<T> MainToWorker<T>(Func<T> action);
}

