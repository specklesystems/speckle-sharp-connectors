using System.Diagnostics.CodeAnalysis;
using Speckle.Connectors.Utils.Operations;

namespace Speckle.Connectors.DUI.Bridge;

public class SyncToUIThread : ISyncToThread
{
  private readonly IBridge _bridge;

  public SyncToUIThread(IBridge bridge)
  {
    _bridge = bridge;
  }

  [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Task Completion Source")]
  public async Task<T> RunOnThread<T>(Func<Task<T>> func) =>
    await _bridge.RunOnMainThreadAsync(func).ConfigureAwait(false);
}
