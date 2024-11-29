namespace Speckle.Connectors.DUI.Eventing;

///<summary>
/// Defines a base class to publish and subscribe to events.
///</summary>
public abstract class EventBase
{
  private readonly List<IEventSubscription> _subscriptions = new List<IEventSubscription>();

  /// <summary>
  /// Allows the SynchronizationContext to be set by the EventAggregator for UI Thread Dispatching
  /// </summary>
  public SynchronizationContext SynchronizationContext { get; set; }

  /// <summary>
  /// Gets the list of current subscriptions.
  /// </summary>
  /// <value>The current subscribers.</value>
  protected ICollection<IEventSubscription> Subscriptions => _subscriptions;

  /// <summary>
  /// Adds the specified <see cref="IEventSubscription"/> to the subscribers' collection.
  /// </summary>
  /// <param name="eventSubscription">The subscriber.</param>
  /// <returns>The <see cref="SubscriptionToken"/> that uniquely identifies every subscriber.</returns>
  /// <remarks>
  /// Adds the subscription to the internal list and assigns it a new <see cref="SubscriptionToken"/>.
  /// </remarks>
  protected virtual SubscriptionToken InternalSubscribe(IEventSubscription eventSubscription)
  {
    if (eventSubscription == null)
    {
      throw new ArgumentNullException(nameof(eventSubscription));
    }

    eventSubscription.SubscriptionToken = new SubscriptionToken(Unsubscribe);

    lock (Subscriptions)
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
  protected virtual void InternalPublish(params object[] arguments)
  {
    List<Action<object[]>> executionStrategies = PruneAndReturnStrategies();
    foreach (var executionStrategy in executionStrategies)
    {
      executionStrategy(arguments);
    }
  }

  /// <summary>
  /// Removes the subscriber matching the <see cref="SubscriptionToken"/>.
  /// </summary>
  /// <param name="token">The <see cref="SubscriptionToken"/> returned by <see cref="EventBase"/> while subscribing to the event.</param>
  public virtual void Unsubscribe(SubscriptionToken token)
  {
    lock (Subscriptions)
    {
      IEventSubscription subscription = Subscriptions.FirstOrDefault(evt => evt.SubscriptionToken == token);
      if (subscription != null)
      {
        Subscriptions.Remove(subscription);
      }
    }
  }

  /// <summary>
  /// Returns <see langword="true"/> if there is a subscriber matching <see cref="SubscriptionToken"/>.
  /// </summary>
  /// <param name="token">The <see cref="SubscriptionToken"/> returned by <see cref="EventBase"/> while subscribing to the event.</param>
  /// <returns><see langword="true"/> if there is a <see cref="SubscriptionToken"/> that matches; otherwise <see langword="false"/>.</returns>
  public virtual bool Contains(SubscriptionToken token)
  {
    lock (Subscriptions)
    {
      IEventSubscription subscription = Subscriptions.FirstOrDefault(evt => evt.SubscriptionToken == token);
      return subscription != null;
    }
  }

  private List<Action<object[]>> PruneAndReturnStrategies()
  {
    List<Action<object[]>> returnList = new List<Action<object[]>>();

    lock (Subscriptions)
    {
      for (var i = Subscriptions.Count - 1; i >= 0; i--)
      {
        Action<object[]>? listItem =
          _subscriptions[i].GetExecutionStrategy();

        if (listItem == null)
        {
          // Prune from main list. Log?
          _subscriptions.RemoveAt(i);
        }
        else
        {
          returnList.Add(listItem);
        }
      }
    }

    return returnList;
  }

  /// <summary>
  /// Forces the PubSubEvent to remove any subscriptions that no longer have an execution strategy.
  /// </summary>
  public void Prune()
  {
    lock (Subscriptions)
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

   public sealed class SubscriptionToken : IEquatable<SubscriptionToken>, IDisposable
    {
        private readonly Guid _token;
        private Action<SubscriptionToken>? _unsubscribeAction;

        /// <summary>
        /// Initializes a new instance of <see cref="SubscriptionToken"/>.
        /// </summary>
        public SubscriptionToken(Action<SubscriptionToken> unsubscribeAction)
        {
            _unsubscribeAction = unsubscribeAction;
            _token = Guid.NewGuid();
        }

        ///<summary>
        ///Indicates whether the current object is equal to another object of the same type.
        ///</summary>
        ///<returns>
        ///<see langword="true"/> if the current object is equal to the <paramref name="other" /> parameter; otherwise, <see langword="false"/>.
        ///</returns>
        ///<param name="other">An object to compare with this object.</param>
        public bool Equals(SubscriptionToken? other)
        {
            if (other == null)
            {
              return false;
            }

            return Equals(_token, other._token);
        }

        public override bool Equals(object? obj)
        {
            if (ReferenceEquals(this, obj))
            {
              return true;
            }

            return Equals(obj as SubscriptionToken);
        }

        public override int GetHashCode()
        {
            return _token.GetHashCode();
        }

        /// <summary>
        /// Disposes the SubscriptionToken, removing the subscription from the corresponding <see cref="EventBase"/>.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1063:ImplementIDisposableCorrectly", Justification = "Should never have need for a finalizer, hence no need for Dispose(bool).")]
        public void Dispose()
        {
            // While the SubscriptionToken class implements IDisposable, in the case of weak subscriptions 
            // (i.e. keepSubscriberReferenceAlive set to false in the Subscribe method) it's not necessary to unsubscribe,
            // as no resources should be kept alive by the event subscription. 
            // In such cases, if a warning is issued, it could be suppressed.

            if (this._unsubscribeAction != null)
            {
                this._unsubscribeAction(this);
                this._unsubscribeAction = null;
            }
        }
    }
   public enum ThreadOption
   {
     PublisherThread,

     /// <summary>
     /// The call is done on the UI thread.
     /// </summary>
     UIThread,

     /// <summary>
     /// The call is done asynchronously on a background thread.
     /// </summary>
     BackgroundThread
   }
