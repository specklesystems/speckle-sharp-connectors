using Speckle.Connectors.Common.Threading;

namespace Speckle.Connectors.DUI.Bridge;

public class ExceptionEvent(IThreadContext threadContext) : SpeckleEvent<Exception>(threadContext);

public class ThreadContextEventSubscription<T>(
  IDelegateReference actionReference,
  IDelegateReference filterReference,
  IThreadContext threadContext
) : EventSubscription<T>(actionReference, filterReference)
{
  public override void InvokeAction(Action<T> action, T payload) =>
    threadContext.RunOnMain(() => action.Invoke(payload));
}

public class SpeckleEvent<T>(IThreadContext threadContext) : PubSubEvent<T>
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
      case ThreadOption.BackgroundThread:
        subscription = new BackgroundEventSubscription<T>(actionReference, filterReference);
        break;
      case ThreadOption.UIThread:
        subscription = new ThreadContextEventSubscription<T>(actionReference, filterReference, threadContext);
        break;
      case ThreadOption.PublisherThread:
      default:
        subscription = new EventSubscription<T>(actionReference, filterReference);
        break;
    }

    return InternalSubscribe(subscription);
  }
}
