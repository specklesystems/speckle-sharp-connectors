namespace Speckle.Connectors.DUI.Eventing;

///<summary>
/// Defines a base class to publish and subscribe to events.
///</summary>
public abstract class EventBase
{
  private readonly List<IEventSubscription> _subscriptions = new();

  protected SubscriptionToken InternalSubscribe(IEventSubscription eventSubscription)
  {
    lock (_subscriptions)
    {
      _subscriptions.Add(eventSubscription);
    }
    return eventSubscription.SubscriptionToken;
  }

  protected async Task InternalPublish(params object[] arguments)
  {
    var executionStrategies = PruneAndReturnStrategies();
    foreach (var executionStrategy in executionStrategies)
    {
      await executionStrategy(arguments);
    }
  }

  private IEnumerable<Func<object[], Task>> PruneAndReturnStrategies()
  {
    lock (_subscriptions)
    {
      for (var i = _subscriptions.Count - 1; i >= 0; i--)
      {
        var listItem = _subscriptions[i].GetExecutionStrategy();

        if (listItem == null)
        {
          // Prune from main list. Log?
          _subscriptions.RemoveAt(i);
        }
        else
        {
          yield return listItem;
        }
      }
    }
  }

  public void Unsubscribe(SubscriptionToken token)
  {
    lock (_subscriptions)
    {
      IEventSubscription? subscription = _subscriptions.FirstOrDefault(evt => evt.SubscriptionToken.Equals(token));
      if (subscription != null)
      {
        _subscriptions.Remove(subscription);
        token.Unsubscribe(); //calling dispose is circular
      }
    }
  }

  public bool Contains(SubscriptionToken token)
  {
    lock (_subscriptions)
    {
      IEventSubscription subscription = _subscriptions.FirstOrDefault(evt => evt.SubscriptionToken == token);
      return subscription != null;
    }
  }

  public void Prune()
  {
    lock (_subscriptions)
    {
      for (var i = _subscriptions.Count - 1; i >= 0; i--)
      {
        if (_subscriptions[i].GetExecutionStrategy() == null)
        {
          _subscriptions.RemoveAt(i);
        }
      }
    }
  }
}
