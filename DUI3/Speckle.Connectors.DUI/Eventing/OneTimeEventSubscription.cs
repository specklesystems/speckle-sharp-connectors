using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.DUI.Eventing;

public class OneTimeEventSubscription<T>(
  DelegateReference actionReference,
  ITopLevelExceptionHandler exceptionHandler,
  SubscriptionToken token,
  bool isOnce
) : EventSubscription<T>(actionReference, exceptionHandler, token)
  where T : notnull
{
  public override async Task InvokeAction(T payload)
  {
    await base.InvokeAction(payload);
    if (isOnce)
    {
      SubscriptionToken.Dispose();
    }
  }
}
