using Speckle.Connectors.Common.Threading;

namespace Speckle.Connectors.DUI.Eventing;

public class MainThreadEventSubscription<T>(
  IDelegateReference actionReference,
  IDelegateReference filterReference,
  IThreadContext threadContext
) : EventSubscription<T>(actionReference, filterReference)
{
  public override void InvokeAction(Action<T> action, T payload) =>
    threadContext.RunOnMain(() => action.Invoke(payload));
}
