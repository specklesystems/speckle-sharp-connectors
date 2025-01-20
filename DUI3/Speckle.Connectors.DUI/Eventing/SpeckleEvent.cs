using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.DUI.Eventing;

public abstract class SpeckleEvent<T>(IThreadContext threadContext, ITopLevelExceptionHandler exceptionHandler)
  : EventBase,
    ISpeckleEvent
  where T : notnull
{
  public string Name { get; } = typeof(T).Name;
  public virtual Task PublishAsync(T payload) => InternalPublish(payload);
  
  
  protected SubscriptionToken SubscribeOnceOrNot(
    Func<T, Task> action,
    ThreadOption threadOption,
    bool isOnce
  )
  {
    IDelegateReference actionReference = new DelegateReference(action);
   
    EventSubscriptionAsync<T> subscription;
    switch (threadOption)
    {
      case ThreadOption.WorkerThread:
        subscription = new WorkerEventSubscriptionAsync<T>(
          actionReference,
          threadContext,
          exceptionHandler,
          isOnce
        );
        break;
      case ThreadOption.MainThread:
        subscription = new MainThreadEventSubscriptionAsync<T>(
          actionReference,
          threadContext,
          exceptionHandler,
          isOnce
        );
        break;
      case ThreadOption.PublisherThread:
      default:
        subscription = new OneTimeEventSubscriptionAsync<T>(actionReference, exceptionHandler, isOnce);
        break;
    }
    return InternalSubscribe(subscription);
  }
  
  protected SubscriptionToken SubscribeOnceOrNot(
    Action<T> action,
    ThreadOption threadOption,
    bool isOnce
  )
  {
    IDelegateReference actionReference = new DelegateReference(action);
   
    EventSubscriptionSync<T> subscription;
    switch (threadOption)
    {
      case ThreadOption.WorkerThread:
        subscription = new WorkerEventSubscriptionSync<T>(
          actionReference,
          threadContext,
          exceptionHandler,
          isOnce
        );
        break;
      case ThreadOption.MainThread:
        subscription = new MainThreadEventSubscriptionSync<T>(
          actionReference,
          threadContext,
          exceptionHandler,
          isOnce
        );
        break;
      case ThreadOption.PublisherThread:
      default:
        subscription = new OneTimeEventSubscriptionSync<T>(actionReference, exceptionHandler, isOnce);
        break;
    }
    return InternalSubscribe(subscription);
  }
}
