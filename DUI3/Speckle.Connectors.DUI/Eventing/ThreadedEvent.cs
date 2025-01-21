using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.DUI.Eventing;

public abstract class ThreadedEvent<T>(IThreadContext threadContext, ITopLevelExceptionHandler exceptionHandler)
  : SpeckleEvent<T>(threadContext, exceptionHandler)
  where T : notnull
{
  public SubscriptionToken Subscribe(Func<T, Task> action, ThreadOption threadOption = ThreadOption.PublisherThread) =>
    Subscribe(action, threadOption, EventFeatures.None);

  public SubscriptionToken Subscribe(Action<T> action, ThreadOption threadOption = ThreadOption.PublisherThread) =>
    Subscribe(action, threadOption, EventFeatures.None);
}
