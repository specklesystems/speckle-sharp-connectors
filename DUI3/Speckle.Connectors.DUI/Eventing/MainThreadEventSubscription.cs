using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.DUI.Eventing;

public class MainThreadEventSubscription<T>(
  IDelegateReference actionReference,
  IDelegateReference filterReference,
  IThreadContext threadContext,
  ITopLevelExceptionHandler exceptionHandler
) : EventSubscription<T>(actionReference, filterReference, exceptionHandler)
{
  public override void InvokeAction(Action<T> action, T payload) => threadContext.RunOnMain(() => action.Invoke(payload));
}
