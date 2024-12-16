using Speckle.InterfaceGenerator;

namespace Speckle.Connectors.Common.Threading;

[GenerateAutoInterface]
public abstract class ThreadContext : IThreadContext
{
  public static bool IsMainThread => Environment.CurrentManagedThreadId == 1 && !Thread.CurrentThread.IsBackground;

  public async ValueTask RunOnThread(Action action, bool useMain)
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
          return new ValueTask<object?>();
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
          return new ValueTask<object?>();
        });
      }
      else
      {
        action();
      }
    }
  }

  public virtual ValueTask<T> RunOnThread<T>(Func<T> action, bool useMain)
  {
    if (useMain)
    {
      if (IsMainThread)
      {
        return new ValueTask<T>(action());
      }

      return WorkerToMain(action);
    }
    if (IsMainThread)
    {
      return MainToWorker(action);
    }

    return new ValueTask<T>(action());
  }

  public async ValueTask RunOnThreadAsync(Func<ValueTask> action, bool useMain)
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

  public ValueTask<T> RunOnThreadAsync<T>(Func<ValueTask<T>> action, bool useMain)
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

  protected abstract ValueTask<T> WorkerToMainAsync<T>(Func<ValueTask<T>> action);

  protected abstract ValueTask<T> MainToWorkerAsync<T>(Func<ValueTask<T>> action);
  protected abstract ValueTask<T> WorkerToMain<T>(Func<T> action);

  protected abstract ValueTask<T> MainToWorker<T>(Func<T> action);
}
