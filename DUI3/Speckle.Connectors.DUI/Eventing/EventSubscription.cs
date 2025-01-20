using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.DUI.Eventing;

public class EventSubscriptionAsync<TPayload>(
  IDelegateReference actionReference,
  ITopLevelExceptionHandler exceptionHandler
) : IEventSubscription
{
  public Func<TPayload, Task>? Action => (Func<TPayload, Task>?)actionReference.Target;

  public SubscriptionToken SubscriptionToken { get; set; }

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

  /// <summary>
  /// Invokes the specified <see cref="System.Action{TPayload}"/> synchronously when not overridden.
  /// </summary>
  /// <param name="action">The action to execute.</param>
  /// <param name="argument">The payload to pass <paramref name="action"/> while invoking it.</param>
  /// <exception cref="ArgumentNullException">An <see cref="ArgumentNullException"/> is thrown if <paramref name="action"/> is null.</exception>
  public virtual async Task InvokeAction(Func<TPayload, Task> action, TPayload argument)
  {
    if (action == null)
    {
      throw new ArgumentNullException(nameof(action));
    }

    await exceptionHandler.CatchUnhandledAsync(() => action(argument));
  }
}

public class EventSubscriptionSync<TPayload>(
  IDelegateReference actionReference,
  ITopLevelExceptionHandler exceptionHandler
) : IEventSubscription
{
  public Action<TPayload>? Action => (Action<TPayload>?)actionReference.Target;

  public SubscriptionToken SubscriptionToken { get; set; }

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
      InvokeAction(action, argument);
      return Task.CompletedTask;
    };
  }

  public virtual Task InvokeAction(Action<TPayload> action, TPayload argument)
  {
    if (action == null)
    {
      throw new ArgumentNullException(nameof(action));
    }

    exceptionHandler.CatchUnhandled(() => action(argument));
    return Task.CompletedTask;
  }
}
