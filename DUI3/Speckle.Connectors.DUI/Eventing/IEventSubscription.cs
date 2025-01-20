namespace Speckle.Connectors.DUI.Eventing;

public interface IEventSubscription
{
  SubscriptionToken SubscriptionToken { get; set; }

  Func<object[], Task>? GetExecutionStrategy();
}
