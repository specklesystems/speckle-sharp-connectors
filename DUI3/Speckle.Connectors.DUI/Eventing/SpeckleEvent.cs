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

  protected SubscriptionToken Subscribe(Func<T, Task> action, ThreadOption threadOption, EventFeatures features)
  {
    features |= EventFeatures.IsAsync;
    var actionReference = new DelegateReference(action, features);
    return Subscribe(actionReference, threadOption, features);
  }

  protected SubscriptionToken Subscribe(Action<T> action, ThreadOption threadOption, EventFeatures features)
  {
    var actionReference = new DelegateReference(action, features);
    return Subscribe(actionReference, threadOption, features);
  }

  [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope")]
  private SubscriptionToken Subscribe(
    DelegateReference actionReference,
    ThreadOption threadOption,
    EventFeatures features
  )
  {
    EventSubscription<T> subscription =
      new(actionReference, threadContext, exceptionHandler, new(Unsubscribe), threadOption, features);
    return InternalSubscribe(subscription);
  }
}
