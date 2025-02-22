using Speckle.InterfaceGenerator;

namespace Speckle.Connectors.Common.Threading;

[GenerateAutoInterface]
public abstract class ThreadContext : IThreadContext
{
  private static readonly Task<object?> s_empty = Task.FromResult<object?>(null);
  public static bool IsMainThread => Environment.CurrentManagedThreadId == 1;

  public async Task RunOnThread(Action action, bool useMain)
  {
    if (useMain)
    {
      if (IsMainThread)
      {
        RunMain(action);
      }
      else
      {
        await WorkerToMainAsync(() =>
        {
          action();
          return s_empty;
        });
      }
    }
    else
    {
      if (IsMainThread)
      {
        await MainToWorkerAsync(() =>
        {
          action();
          return s_empty;
        });
      }
      else
      {
        RunMain(action);
      }
    }
  }

  public virtual Task<T> RunOnThread<T>(Func<T> action, bool useMain)
  {
    if (useMain)
    {
      if (IsMainThread)
      {
        return RunMainAsync(action);
      }

      return WorkerToMain(action);
    }
    if (IsMainThread)
    {
      return MainToWorker(action);
    }

    return RunMainAsync(action);
  }

  public async Task RunOnThreadAsync(Func<Task> action, bool useMain)
  {
    if (useMain)
    {
      if (IsMainThread)
      {
        await RunMainAsync(action);
      }
      else
      {
        await WorkerToMainAsync<object?>(async () =>
        {
          await action();
          return Task.FromResult<object?>(null);
        });
      }
    }
    else
    {
      if (IsMainThread)
      {
        await MainToWorkerAsync<object?>(async () =>
        {
          await action();
          return Task.FromResult<object?>(null);
        });
      }
      else
      {
        if (useMain)
        {
          await RunMainAsync(action);
        }
        else
        {
          await action();
        }
      }
    }
  }

  public Task<T> RunOnThreadAsync<T>(Func<Task<T>> action, bool useMain)
  {
    if (useMain)
    {
      if (IsMainThread)
      {
        return RunMainAsync(action);
      }

      return WorkerToMainAsync(action);
    }
    if (IsMainThread)
    {
      return MainToWorkerAsync(action);
    }
    return RunMainAsync(action);
  }

  protected abstract Task<T> WorkerToMainAsync<T>(Func<Task<T>> action);

  protected abstract Task<T> MainToWorkerAsync<T>(Func<Task<T>> action);
  protected abstract Task<T> WorkerToMain<T>(Func<T> action);

  protected abstract Task<T> MainToWorker<T>(Func<T> action);

  protected virtual void RunMain(Action action) => action();

  protected virtual Task<T> RunMainAsync<T>(Func<T> action) => Task.FromResult(action());

  protected virtual Task RunMainAsync(Func<Task> action) => Task.FromResult(action());

  protected virtual Task<T> RunMainAsync<T>(Func<Task<T>> action) => action();
}
