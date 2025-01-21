using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.DUI.Eventing;

public class EventSubscription<TPayload>(
  DelegateReference actionReference,
  ITopLevelExceptionHandler exceptionHandler,
  SubscriptionToken token
) : IEventSubscription
  where TPayload : notnull
{
  public SubscriptionToken SubscriptionToken => token;

  public virtual Func<object[], Task>? GetExecutionStrategy()
  {
    if (!actionReference.IsAlive)
    {
      return null;
    }
    return async arguments =>
    {
      TPayload argument = (TPayload)arguments[0];
      await InvokeAction(argument);
    };
  }

  public virtual async Task InvokeAction(TPayload argument) =>
    await exceptionHandler.CatchUnhandledAsync(() => actionReference.Invoke(argument));
}
