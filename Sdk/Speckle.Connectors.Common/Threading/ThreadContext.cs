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
        action();
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
        action();
      }
    }
  }

  public virtual Task<T> RunOnThread<T>(Func<T> action, bool useMain)
  {
    if (useMain)
    {
      if (IsMainThread)
      {
        return Task.FromResult(action());
      }

      return WorkerToMain(action);
    }
    if (IsMainThread)
    {
      return MainToWorker(action);
    }

    return Task.FromResult(action());
  }

  public async Task RunOnThreadAsync(Func<Task> action, bool useMain)
  {
    if (useMain)
    {
      if (IsMainThread)
      {
        await action();
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
        await action();
      }
    }
  }

  public Task<T> RunOnThreadAsync<T>(Func<Task<T>> action, bool useMain)
  {
    if (useMain)
    {
      if (IsMainThread)
      {
        return action();
      }

      return WorkerToMainAsync(action);
    }
    if (IsMainThread)
    {
      return MainToWorkerAsync(action);
    }
    return action();
  }

  protected abstract Task<T> WorkerToMainAsync<T>(Func<Task<T>> action);

  protected abstract Task<T> MainToWorkerAsync<T>(Func<Task<T>> action);
  protected abstract Task<T> WorkerToMain<T>(Func<T> action);

  protected abstract Task<T> MainToWorker<T>(Func<T> action);
}
