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
  public override void InvokeAction(Action<T> action, T payload) => threadContext.RunOnMain(() => action.Invoke(payload));
}
