using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.DUI.Eventing;

public class OneTimeEventSubscriptionAsync<T>(
  IDelegateReference actionReference,
  ITopLevelExceptionHandler exceptionHandler,
  bool isOnce
) : EventSubscriptionAsync<T>(actionReference, exceptionHandler)
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

public class OneTimeEventSubscriptionSync<T>(
  IDelegateReference actionReference,
  ITopLevelExceptionHandler exceptionHandler,
  bool isOnce
) : EventSubscriptionSync<T>(actionReference, exceptionHandler)
{
  public override Task InvokeAction(Action<T> action, T payload)
  {
    action.Invoke(payload);
    if (isOnce)
    {
      SubscriptionToken.Dispose();
    }
    return Task.CompletedTask;
  }
}
