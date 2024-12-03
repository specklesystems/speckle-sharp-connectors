using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.DUI.Eventing;

public class OneTimeEventSubscription<T>(
  IDelegateReference actionReference,
  IDelegateReference filterReference,
  ITopLevelExceptionHandler exceptionHandler,
  bool isOnce
) : EventSubscription<T>(actionReference, filterReference, exceptionHandler)
{
  public override void InvokeAction(Action<T> action, T payload)
  {
    action.Invoke(payload);
    if (isOnce)
    {
      SubscriptionToken.Dispose();
    }
  }
}
