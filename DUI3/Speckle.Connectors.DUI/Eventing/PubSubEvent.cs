namespace Speckle.Connectors.DUI.Eventing;

/// <summary>
/// Defines a class that manages publication and subscription to events.
/// </summary>
/// <typeparam name="TPayload">The type of message that will be passed to the subscribers.</typeparam>
public abstract class PubSubEvent<TPayload> : EventBase
  where TPayload : notnull
{
  public abstract SubscriptionToken Subscribe(
    Func<TPayload, Task> action,
    ThreadOption threadOption = ThreadOption.PublisherThread,
    bool keepSubscriberReferenceAlive = false,
    Predicate<TPayload>? filter = null
  );

  /// <summary>
  /// Publishes the <see cref="PubSubEvent{TPayload}"/>.
  /// </summary>
  /// <param name="payload">Message to pass to the subscribers.</param>
  public virtual Task PublishAsync(TPayload payload) => InternalPublish(payload);

  /// <summary>
  /// Removes the first subscriber matching <see cref="Action{TPayload}"/> from the subscribers' list.
  /// </summary>
  /// <param name="subscriber">The <see cref="Action{TPayload}"/> used when subscribing to the event.</param>
  public void Unsubscribe(Func<TPayload, Task> subscriber)
  {
    lock (Subscriptions)
    {
      IEventSubscription? eventSubscription = Subscriptions
        .Cast<EventSubscription<TPayload>>()
        .FirstOrDefault(evt => evt.Action == subscriber);
      if (eventSubscription != null)
      {
        Subscriptions.Remove(eventSubscription);
      }
    }
  }

  /// <summary>
  /// Returns <see langword="true"/> if there is a subscriber matching <see cref="Action{TPayload}"/>.
  /// </summary>
  /// <param name="subscriber">The <see cref="Action{TPayload}"/> used when subscribing to the event.</param>
  /// <returns><see langword="true"/> if there is an <see cref="Action{TPayload}"/> that matches; otherwise <see langword="false"/>.</returns>
  public bool Contains(Func<TPayload, Task> subscriber)
  {
    IEventSubscription? eventSubscription;
    lock (Subscriptions)
    {
      eventSubscription = Subscriptions
        .Cast<EventSubscription<TPayload>>()
        .FirstOrDefault(evt => evt.Action == subscriber);
    }
    return eventSubscription != null;
  }
}
