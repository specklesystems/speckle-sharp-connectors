using System.Diagnostics.CodeAnalysis;
using Speckle.Connectors.Common.Operations;

namespace Speckle.Connectors.DUI.Bridge;

public class SyncToUIThread : ISyncToThread
{
  private readonly IBrowserBridge _bridge;

  public SyncToUIThread(IBrowserBridge bridge)
  {
    _bridge = bridge;
    _bridge.TopLevelExceptionHandler.AllowUseWithoutBrowser = true; //Since this bridge is NEVER associated with a binding, we can't ever get toasts from this boy! A very fragile design!
  }

  [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Task Completion Source")]
  public Task<T> RunOnThread<T>(Func<T> func)
  {
    TaskCompletionSource<T> tcs = new();

    _bridge.RunOnMainThread(() =>
    {
      try
      {
        T result = func.Invoke();
        tcs.SetResult(result);
      }
      catch (Exception ex)
      {
        tcs.SetException(ex);
      }
    });

    return tcs.Task;
  }
}
