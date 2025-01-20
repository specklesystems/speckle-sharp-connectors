using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.DUI.Eventing;

public class EventSubscriptionSync<TPayload>(
  IDelegateReference actionReference,
  ITopLevelExceptionHandler exceptionHandler,
  SubscriptionToken token
) : IEventSubscription
{
  public Action<TPayload>? Action => (Action<TPayload>?)actionReference.Target;

  public SubscriptionToken SubscriptionToken => token;

  public virtual Func<object[], Task>? GetExecutionStrategy()
  {
    var action = Action;
    if (action is null)
    {
      return null;
    }
    return arguments =>
    {
      TPayload argument = (TPayload)arguments[0];
      return InvokeAction(action, argument);
    };
  }

  public virtual Task InvokeAction(Action<TPayload> action, TPayload argument)
  {
    exceptionHandler.CatchUnhandled(() => action(argument));
    return Task.CompletedTask;
  }
}
