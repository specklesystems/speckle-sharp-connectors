using System.Diagnostics.CodeAnalysis;
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

  protected SubscriptionToken SubscribeOnceOrNot(Func<T, Task> action, ThreadOption threadOption, bool isOnce)
  {
    var actionReference = new DelegateReference(action, true);
    return SubscribeOnceOrNot(actionReference, threadOption, isOnce);
  }

  protected SubscriptionToken SubscribeOnceOrNot(Action<T> action, ThreadOption threadOption, bool isOnce)
  {
    var actionReference = new DelegateReference(action, false);
    return SubscribeOnceOrNot(actionReference, threadOption, isOnce);
  }

  [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope")]
  private SubscriptionToken SubscribeOnceOrNot(
    DelegateReference actionReference,
    ThreadOption threadOption,
    bool isOnce
  )
  {
    EventSubscription<T> subscription;
    switch (threadOption)
    {
      case ThreadOption.WorkerThread:
        subscription = new WorkerEventSubscription<T>(
          actionReference,
          threadContext,
          exceptionHandler,
          new(Unsubscribe),
          isOnce
        );
        break;
      case ThreadOption.MainThread:
        subscription = new MainThreadEventSubscription<T>(
          actionReference,
          threadContext,
          exceptionHandler,
          new(Unsubscribe),
          isOnce
        );
        break;
      case ThreadOption.PublisherThread:
      default:
        subscription = new OneTimeEventSubscription<T>(actionReference, exceptionHandler, new(Unsubscribe), isOnce);
        break;
    }
    return InternalSubscribe(subscription);
  }
}
