using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.DUI.Eventing;

public class EventSubscriptionAsync<TPayload>(
  IDelegateReference actionReference,
  ITopLevelExceptionHandler exceptionHandler,
  SubscriptionToken token
) : IEventSubscription
{
  public Func<TPayload, Task>? Action => (Func<TPayload, Task>?)actionReference.Target;

  public SubscriptionToken SubscriptionToken => token;

  public virtual Func<object[], Task>? GetExecutionStrategy()
  {
    Func<TPayload, Task>? action = Action;
    if (action is null)
    {
      return null;
    }
    return async arguments =>
    {
      TPayload argument = (TPayload)arguments[0];
      await InvokeAction(action, argument);
    };
  }

  public virtual async Task InvokeAction(Func<TPayload, Task> action, TPayload argument) =>
    await exceptionHandler.CatchUnhandledAsync(() => action(argument));
}
