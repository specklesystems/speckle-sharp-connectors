using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.DUI.Eventing;

public class MainThreadEventSubscription<T>(
  IDelegateReference actionReference,
  IDelegateReference filterReference,
  IThreadContext threadContext,
  ITopLevelExceptionHandler exceptionHandler,
  bool isOnce
) : OneTimeEventSubscription<T>(actionReference, filterReference, exceptionHandler, isOnce)
{
  public override Task InvokeAction(Func<T, Task> action, T payload) =>
     threadContext.RunOnMainAsync(() => action.Invoke(payload));
}
