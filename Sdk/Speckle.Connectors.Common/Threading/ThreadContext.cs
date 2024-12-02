using Speckle.InterfaceGenerator;

namespace Speckle.Connectors.Common.Threading;

[GenerateAutoInterface]
public abstract class ThreadContext : IThreadContext
{
  private static readonly Task<object?> s_completedTask = Task.FromResult<object?>(null);

  public static bool IsMainThread => Environment.CurrentManagedThreadId == 1 && !Thread.CurrentThread.IsBackground;

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
            return s_completedTask;
          })
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
          })
          .BackToAny();
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
        await action().BackToCurrent();
      }
      else
      {
        await WorkerToMainAsync<object?>(async () =>
          {
            await action().BackToCurrent();
            return null;
          })
          .BackToCurrent();
      }
    }
    else
    {
      if (IsMainThread)
      {
        await MainToWorkerAsync<object?>(async () =>
          {
            await action().BackToCurrent();
            return null;
          })
          .BackToCurrent();
      }
      else
      {
        await action().BackToCurrent();
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
