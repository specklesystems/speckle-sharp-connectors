namespace Speckle.Connectors.DUI.Eventing;

/// <summary>
/// Defines a class that manages publication and subscription to events.
/// </summary>
/// <typeparam name="TPayload">The type of message that will be passed to the subscribers.</typeparam>
public abstract class PubSubEvent<TPayload> : EventBase
  where TPayload : notnull
{
  /// <summary>
  /// Subscribes a delegate to an event that will be published on the <see cref="ThreadOption.PublisherThread"/>.
  /// <see cref="PubSubEvent{TPayload}"/> will maintain a <see cref="WeakReference"/> to the target of the supplied <paramref name="action"/> delegate.
  /// </summary>
  /// <param name="action">The delegate that gets executed when the event is published.</param>
  /// <returns>A <see cref="SubscriptionToken"/> that uniquely identifies the added subscription.</returns>
  /// <remarks>
  /// The PubSubEvent collection is thread-safe.
  /// </remarks>
  public SubscriptionToken Subscribe(Action<TPayload> action) => Subscribe(action, ThreadOption.PublisherThread);

  /// <summary>
  /// Subscribes a delegate to an event that will be published on the <see cref="ThreadOption.PublisherThread"/>
  /// </summary>
  /// <param name="action">The delegate that gets executed when the event is raised.</param>
  /// <param name="filter">Filter to evaluate if the subscriber should receive the event.</param>
  /// <returns>A <see cref="SubscriptionToken"/> that uniquely identifies the added subscription.</returns>
  public virtual SubscriptionToken Subscribe(Action<TPayload> action, Predicate<TPayload> filter) =>
    Subscribe(action, ThreadOption.PublisherThread, false, filter);

  /// <summary>
  /// Subscribes a delegate to an event.
  /// PubSubEvent will maintain a <see cref="WeakReference"/> to the Target of the supplied <paramref name="action"/> delegate.
  /// </summary>
  /// <param name="action">The delegate that gets executed when the event is raised.</param>
  /// <param name="threadOption">Specifies on which thread to receive the delegate callback.</param>
  /// <returns>A <see cref="SubscriptionToken"/> that uniquely identifies the added subscription.</returns>
  /// <remarks>
  /// The PubSubEvent collection is thread-safe.
  /// </remarks>
  public SubscriptionToken Subscribe(Action<TPayload> action, ThreadOption threadOption) =>
    Subscribe(action, threadOption, false);

  /// <summary>
  /// Subscribes a delegate to an event that will be published on the <see cref="ThreadOption.PublisherThread"/>.
  /// </summary>
  /// <param name="action">The delegate that gets executed when the event is published.</param>
  /// <param name="keepSubscriberReferenceAlive">When <see langword="true"/>, the <see cref="PubSubEvent{TPayload}"/> keeps a reference to the subscriber so it does not get garbage collected.</param>
  /// <returns>A <see cref="SubscriptionToken"/> that uniquely identifies the added subscription.</returns>
  /// <remarks>
  /// If <paramref name="keepSubscriberReferenceAlive"/> is set to <see langword="false" />, <see cref="PubSubEvent{TPayload}"/> will maintain a <see cref="WeakReference"/> to the Target of the supplied <paramref name="action"/> delegate.
  /// If not using a WeakReference (<paramref name="keepSubscriberReferenceAlive"/> is <see langword="true" />), the user must explicitly call Unsubscribe for the event when disposing the subscriber in order to avoid memory leaks or unexpected behavior.
  /// <para/>
  /// The PubSubEvent collection is thread-safe.
  /// </remarks>
  public SubscriptionToken Subscribe(Action<TPayload> action, bool keepSubscriberReferenceAlive) =>
    Subscribe(action, ThreadOption.PublisherThread, keepSubscriberReferenceAlive);

  /// <summary>
  /// Subscribes a delegate to an event.
  /// </summary>
  /// <param name="action">The delegate that gets executed when the event is published.</param>
  /// <param name="threadOption">Specifies on which thread to receive the delegate callback.</param>
  /// <param name="keepSubscriberReferenceAlive">When <see langword="true"/>, the <see cref="PubSubEvent{TPayload}"/> keeps a reference to the subscriber so it does not get garbage collected.</param>
  /// <returns>A <see cref="SubscriptionToken"/> that uniquely identifies the added subscription.</returns>
  /// <remarks>
  /// If <paramref name="keepSubscriberReferenceAlive"/> is set to <see langword="false" />, <see cref="PubSubEvent{TPayload}"/> will maintain a <see cref="WeakReference"/> to the Target of the supplied <paramref name="action"/> delegate.
  /// If not using a WeakReference (<paramref name="keepSubscriberReferenceAlive"/> is <see langword="true" />), the user must explicitly call Unsubscribe for the event when disposing the subscriber in order to avoid memory leaks or unexpected behavior.
  /// <para/>
  /// The PubSubEvent collection is thread-safe.
  /// </remarks>
  public SubscriptionToken Subscribe(
    Action<TPayload> action,
    ThreadOption threadOption,
    bool keepSubscriberReferenceAlive
  ) => Subscribe(action, threadOption, keepSubscriberReferenceAlive, null);

  
  
  /// <summary>
  /// Subscribes a delegate to an event.
  /// </summary>
  /// <param name="action">The delegate that gets executed when the event is published.</param>
  /// <param name="threadOption">Specifies on which thread to receive the delegate callback.</param>
  /// <param name="keepSubscriberReferenceAlive">When <see langword="true"/>, the <see cref="PubSubEvent{TPayload}"/> keeps a reference to the subscriber so it does not get garbage collected.</param>
  /// <param name="filter">Filter to evaluate if the subscriber should receive the event.</param>
  /// <returns>A <see cref="SubscriptionToken"/> that uniquely identifies the added subscription.</returns>
  /// <remarks>
  /// If <paramref name="keepSubscriberReferenceAlive"/> is set to <see langword="false" />, <see cref="PubSubEvent{TPayload}"/> will maintain a <see cref="WeakReference"/> to the Target of the supplied <paramref name="action"/> delegate.
  /// If not using a WeakReference (<paramref name="keepSubscriberReferenceAlive"/> is <see langword="true" />), the user must explicitly call Unsubscribe for the event when disposing the subscriber in order to avoid memory leaks or unexpected behavior.
  ///
  /// The PubSubEvent collection is thread-safe.
  /// </remarks>
  public abstract SubscriptionToken Subscribe(
    Action<TPayload> action,
    ThreadOption threadOption,
    bool keepSubscriberReferenceAlive,
    Predicate<TPayload>? filter
  );

  /// <summary>
  /// Publishes the <see cref="PubSubEvent{TPayload}"/>.
  /// </summary>
  /// <param name="payload">Message to pass to the subscribers.</param>
  public virtual void Publish(TPayload payload) => InternalPublish(payload);

  /// <summary>
  /// Removes the first subscriber matching <see cref="Action{TPayload}"/> from the subscribers' list.
  /// </summary>
  /// <param name="subscriber">The <see cref="Action{TPayload}"/> used when subscribing to the event.</param>
  public virtual void Unsubscribe(Action<TPayload> subscriber)
  {
    lock (Subscriptions)
    {
      IEventSubscription eventSubscription = Subscriptions
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
  public virtual bool Contains(Action<TPayload> subscriber)
  {
    IEventSubscription eventSubscription;
    lock (Subscriptions)
    {
      eventSubscription = Subscriptions
        .Cast<EventSubscription<TPayload>>()
        .FirstOrDefault(evt => evt.Action == subscriber);
    }
    return eventSubscription != null;
  }
}
