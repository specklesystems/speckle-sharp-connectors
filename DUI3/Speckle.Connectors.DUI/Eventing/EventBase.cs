namespace Speckle.Connectors.DUI.Eventing;

///<summary>
/// Defines a base class to publish and subscribe to events.
///</summary>
public abstract class EventBase : IDisposable
{
  private readonly SemaphoreSlim _semaphoreSlim = new(1, 1);
  private readonly List<IEventSubscription> _subscriptions = new();
  protected ICollection<IEventSubscription> Subscriptions => _subscriptions;

  protected virtual void Dispose(bool isDisposing)
  {
    if (isDisposing)
    {
      _semaphoreSlim.Dispose();
    }
  }
  
  public void Dispose()
  {
    Dispose(true);
    GC.SuppressFinalize(this);
  }

  ~EventBase()
  {
    Dispose(false);
  }

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

    _semaphoreSlim.Wait();
    try
    {
      Subscriptions.Add(eventSubscription);
    }
    finally
    {
      _semaphoreSlim.Release();
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
    _semaphoreSlim.Wait();
    try
    {
      IEventSubscription? subscription = Subscriptions.FirstOrDefault(evt => evt.SubscriptionToken.Equals(token));
      if (subscription != null)
      {
        Subscriptions.Remove(subscription);
        token.Unsubscribe(); //calling dispose is circular
      }
    }
    finally
    {
      _semaphoreSlim.Release();
    }
  }

  /// <summary>
  /// Returns <see langword="true"/> if there is a subscriber matching <see cref="SubscriptionToken"/>.
  /// </summary>
  /// <param name="token">The <see cref="SubscriptionToken"/> returned by <see cref="EventBase"/> while subscribing to the event.</param>
  /// <returns><see langword="true"/> if there is a <see cref="SubscriptionToken"/> that matches; otherwise <see langword="false"/>.</returns>
  public  bool Contains(SubscriptionToken token)
  {
    _semaphoreSlim.Wait();
    try
    {
      IEventSubscription subscription = Subscriptions.FirstOrDefault(evt => evt.SubscriptionToken == token);
      return subscription != null;
    }
    finally
    {
      _semaphoreSlim.Release();
    }
  }

  private IEnumerable<Func<object[], Task>> PruneAndReturnStrategies()
  {
    _semaphoreSlim.Wait();
    try
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
    finally
    {
      _semaphoreSlim.Release();
    }
  }

  /// <summary>
  /// Forces the PubSubEvent to remove any subscriptions that no longer have an execution strategy.
  /// </summary>
  public void Prune()
  {
    _semaphoreSlim.Wait();
    try
    {
      for (var i = Subscriptions.Count - 1; i >= 0; i--)
      {
        if (_subscriptions[i].GetExecutionStrategy() == null)
        {
          _subscriptions.RemoveAt(i);
        }
      }
    }
    finally
    {
      _semaphoreSlim.Release();
    }
  }
}
