using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.DUI.Eventing;

public abstract class ThreadedEvent<T>(IThreadContext threadContext, ITopLevelExceptionHandler exceptionHandler)
  : PubSubEvent<T>,
    ISpeckleEvent
  where T : notnull
{
  public string Name { get; } = typeof(T).Name;

  public override SubscriptionToken Subscribe(
    Func<T, Task> action,
    ThreadOption threadOption = ThreadOption.PublisherThread,
    bool keepSubscriberReferenceAlive = false,
    Predicate<T>? filter = null
  )
  {
    return SubscribeOnceOrNot(t => action(t), threadOption, keepSubscriberReferenceAlive, filter, false);
  }

  protected SubscriptionToken SubscribeOnceOrNot(
    Action<T> action,
    ThreadOption threadOption,
    bool keepSubscriberReferenceAlive,
    Predicate<T>? filter,
    bool isOnce
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
        subscription = new WorkerEventSubscription<T>(
          actionReference,
          filterReference,
          threadContext,
          exceptionHandler,
          isOnce
        );
        break;
      case ThreadOption.MainThread:
        subscription = new MainThreadEventSubscription<T>(
          actionReference,
          filterReference,
          threadContext,
          exceptionHandler,
          isOnce
        );
        break;
      case ThreadOption.PublisherThread:
      default:
        subscription = new OneTimeEventSubscription<T>(actionReference, filterReference, exceptionHandler, isOnce);
        break;
    }
    return InternalSubscribe(subscription);
  }
}
