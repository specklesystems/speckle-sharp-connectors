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

  public void RunOnMainThread(Action action)
  {
    if (IsMainThread)
    {
      RunContext(action);
      return;
    }
    _mainThreadContext.Post(
      _ =>
      {
        RunContext(action);
      },
      null
    );
  }

  public async Task RunOnMainThreadAsync(Func<Task> action)
  {
    if (IsMainThread)
    {
      await action.Invoke().ConfigureAwait(false);
      return;
    }
    await RunOnMainThreadAsync<object?>(async () =>
      {
        await action.Invoke().ConfigureAwait(false);
        return null;
      })
      .ConfigureAwait(false);
  }

  public virtual Task<T> RunContext<T>(Func<Task<T>> action) => action();

  [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "TaskCompletionSource")]
  public Task<T> RunOnMainThreadAsync<T>(Func<Task<T>> action)
  {
    if (IsMainThread)
    {
      return RunContext(action);
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
}
