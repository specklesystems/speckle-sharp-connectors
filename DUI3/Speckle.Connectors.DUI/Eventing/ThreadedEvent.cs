using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.DUI.Eventing;

public abstract class ThreadedEvent<T>(IThreadContext threadContext, ITopLevelExceptionHandler exceptionHandler) : PubSubEvent<T>, ISpeckleEvent
  where T : notnull
{
  public string Name { get; } = typeof(T).Name;
  public SubscriptionToken Subscribe(
    Func<Task<T>> action,
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
        subscription = new WorkerEventSubscription<T>(actionReference, filterReference, threadContext, exceptionHandler);
        break;
      case ThreadOption.MainThread:
        subscription = new MainThreadEventSubscription<T>(actionReference, filterReference, threadContext, exceptionHandler);
        break;
      case ThreadOption.PublisherThread:
      default:
        subscription = new EventSubscription<T>(actionReference, filterReference, exceptionHandler);
        break;
    }

    return InternalSubscribe(subscription);
  }
  
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
        subscription = new WorkerEventSubscription<T>(actionReference, filterReference, threadContext, exceptionHandler);
        break;
      case ThreadOption.MainThread:
        subscription = new MainThreadEventSubscription<T>(actionReference, filterReference, threadContext, exceptionHandler);
        break;
      case ThreadOption.PublisherThread:
      default:
        subscription = new EventSubscription<T>(actionReference, filterReference, exceptionHandler);
        break;
    }

    return InternalSubscribe(subscription);
  }
}
