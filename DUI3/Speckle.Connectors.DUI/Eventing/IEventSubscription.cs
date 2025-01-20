namespace Speckle.Connectors.DUI.Eventing;

public interface IEventSubscription
{
  SubscriptionToken SubscriptionToken { get; }

  Func<object[], Task>? GetExecutionStrategy();
}
