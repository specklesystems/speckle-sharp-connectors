using Speckle.Connectors.Common.Threading;

namespace Speckle.Connectors.DUI.Eventing;

public class ThreadedEvent<T>(IThreadContext threadContext) : PubSubEvent<T>
  where T : notnull
{
  public override SubscriptionToken Subscribe(
    Action<T> action,
    ThreadOption threadOption,
    bool keepSubscriberReferenceAlive,
    Predicate<T>? filter
  )
  {
    IDelegateReference actionReference = new DelegateReference(action, keepSubscriberReferenceAlive);
    IDelegateReference filterReference;
    if (filter != null)
    {
      filterReference = new DelegateReference(filter, keepSubscriberReferenceAlive);
    }
    else
    {
      filterReference = new DelegateReference(
        new Predicate<T>(
          delegate
          {
            return true;
          }
        ),
        true
      );
    }
    EventSubscription<T> subscription;
    switch (threadOption)
    {
      case ThreadOption.WorkerThread:
        subscription = new WorkerEventSubscription<T>(actionReference, filterReference, threadContext);
        break;
      case ThreadOption.MainThread:
        subscription = new MainThreadEventSubscription<T>(actionReference, filterReference, threadContext);
        break;
      case ThreadOption.PublisherThread:
      default:
        subscription = new EventSubscription<T>(actionReference, filterReference);
        break;
    }

    return InternalSubscribe(subscription);
  }
}
