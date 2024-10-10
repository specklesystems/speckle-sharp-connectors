using System.Diagnostics.CodeAnalysis;
using Speckle.Connectors.Common.Operations;

namespace Speckle.Connectors.DUI.Bridge;

public class SyncToUIThread : ISyncToThread
{
  private readonly IBrowserBridge _bridge;

  public SyncToUIThread(IBrowserBridge bridge)
  {
    _bridge = bridge;
  }

  [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Task Completion Source")]
  public async Task<T> RunOnThread<T>(Func<Task<T>> func) =>
    await _bridge.RunOnMainThreadAsync(func).ConfigureAwait(false);
}
