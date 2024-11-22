using System.Diagnostics.CodeAnalysis;
using Speckle.InterfaceGenerator;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.DUI.Threading;

[GenerateAutoInterface]
public class MainThreadContext: IMainThreadContext
{
  private readonly SynchronizationContext _mainThreadContext;

  public MainThreadContext( )
  {
    _mainThreadContext = SynchronizationContext.Current.NotNull("No UI thread to capture?");
  }
  
  public void RunOnMainThread(Action action) =>
    _mainThreadContext.Post(
      _ =>
      {
        action();
      },
      null
    );

  public async Task RunOnMainThreadAsync(Func<Task> action) =>
    await RunOnMainThreadAsync<object?>(async () =>
      {
        await action.Invoke().ConfigureAwait(false);
        return null;
      })
      .ConfigureAwait(false);
  
  [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "TaskCompletionSource")]
  public Task<T> RunOnMainThreadAsync<T>(Func<Task<T>> action)
  {
    TaskCompletionSource<T> tcs = new();

    _mainThreadContext.Post(
      async _ =>
      {
        try
        {
          T result = await action.Invoke().ConfigureAwait(false);
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
