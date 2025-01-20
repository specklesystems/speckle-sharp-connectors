using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.DUI.Eventing;

public class OneTimeEventSubscription<T>(
  IDelegateReference actionReference,
  IDelegateReference filterReference,
  ITopLevelExceptionHandler exceptionHandler,
  bool isOnce
) : EventSubscription<T>(actionReference, filterReference, exceptionHandler)
{
  public override async Task InvokeAction(Func<T, Task> action, T payload)
  {
    await action.Invoke(payload);
    if (isOnce)
    {
      SubscriptionToken.Dispose();
    }
  }
}
