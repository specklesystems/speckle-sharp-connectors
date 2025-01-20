namespace Speckle.Connectors.DUI.Eventing;

///<summary>
/// Defines a base class to publish and subscribe to events.
///</summary>
public abstract class EventBase
{
  private readonly List<IEventSubscription> _subscriptions = new();
  protected ICollection<IEventSubscription> Subscriptions => _subscriptions;


  /// <summary>
  /// Adds the specified <see cref="IEventSubscription"/> to the subscribers' collection.
  /// </summary>
  /// <param name="eventSubscription">The subscriber.</param>
  /// <returns>The <see cref="SubscriptionToken"/> that uniquely identifies every subscriber.</returns>
  /// <remarks>
  /// Adds the subscription to the internal list and assigns it a new <see cref="SubscriptionToken"/>.
  /// </remarks>
  protected SubscriptionToken InternalSubscribe(IEventSubscription eventSubscription)
  {
    if (eventSubscription == null)
    {
      throw new ArgumentNullException(nameof(eventSubscription));
    }

    eventSubscription.SubscriptionToken = new SubscriptionToken(Unsubscribe);

    lock(Subscriptions)
    {
      Subscriptions.Add(eventSubscription);
    }
    return eventSubscription.SubscriptionToken;
  }

  /// <summary>
  /// Calls all the execution strategies exposed by the list of <see cref="IEventSubscription"/>.
  /// </summary>
  /// <param name="arguments">The arguments that will be passed to the listeners.</param>
  /// <remarks>Before executing the strategies, this class will prune all the subscribers from the
  /// list that return a <see langword="null" /> <see cref="Action{T}"/> when calling the
  /// <see cref="IEventSubscription.GetExecutionStrategy"/> method.</remarks>
  protected async Task InternalPublish(params object[] arguments)
  {
    var executionStrategies = PruneAndReturnStrategies();
    foreach (var executionStrategy in executionStrategies)
    {
      await executionStrategy(arguments);
    }
  }

  /// <summary>
  /// Removes the subscriber matching the <see cref="SubscriptionToken"/>.
  /// </summary>
  /// <param name="token">The <see cref="SubscriptionToken"/> returned by <see cref="EventBase"/> while subscribing to the event.</param>
  public void Unsubscribe(SubscriptionToken token)
  {
    lock(Subscriptions)
    {
      IEventSubscription? subscription = Subscriptions.FirstOrDefault(evt => evt.SubscriptionToken.Equals(token));
      if (subscription != null)
      {
        Subscriptions.Remove(subscription);
        token.Unsubscribe(); //calling dispose is circular
      }
    }
  }

  /// <summary>
  /// Returns <see langword="true"/> if there is a subscriber matching <see cref="SubscriptionToken"/>.
  /// </summary>
  /// <param name="token">The <see cref="SubscriptionToken"/> returned by <see cref="EventBase"/> while subscribing to the event.</param>
  /// <returns><see langword="true"/> if there is a <see cref="SubscriptionToken"/> that matches; otherwise <see langword="false"/>.</returns>
  public  bool Contains(SubscriptionToken token)
  {
   
    lock(Subscriptions)
    {
      IEventSubscription subscription = Subscriptions.FirstOrDefault(evt => evt.SubscriptionToken == token);
      return subscription != null;
    }
  }

  private IEnumerable<Func<object[], Task>> PruneAndReturnStrategies()
  {
    lock(Subscriptions)
    {
      for (var i = Subscriptions.Count - 1; i >= 0; i--)
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

  /// <summary>
  /// Forces the PubSubEvent to remove any subscriptions that no longer have an execution strategy.
  /// </summary>
  public void Prune()
  {
    lock(Subscriptions)
    {
      for (var i = Subscriptions.Count - 1; i >= 0; i--)
      {
        if (_subscriptions[i].GetExecutionStrategy() == null)
        {
          _subscriptions.RemoveAt(i);
        }
      }
    }
  }
}
