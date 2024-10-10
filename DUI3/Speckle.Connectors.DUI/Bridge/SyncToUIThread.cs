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
  public async Task<T> RunOnThread<T>(Func<Task<T>> func) =>
    await _bridge.RunOnMainThreadAsync(func).ConfigureAwait(false);
}
