using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.DUI.Eventing;

public abstract class PeriodicThreadedEvent(IThreadContext threadContext, ITopLevelExceptionHandler exceptionHandler)
  : SpeckleEvent<object>(threadContext, exceptionHandler)
{
  public SubscriptionToken SubscribePeriodic(
    TimeSpan period,
    Func<object, Task> action,
    ThreadOption threadOption = ThreadOption.PublisherThread
  )
  {
    var token = Subscribe(action, threadOption, EventFeatures.IsAsync);
    Task.Factory.StartNew(
        async () =>
        {
          while (token.IsActive)
          {
            await Task.Delay(period);
            await InternalPublish(new object());
          }
        },
        default,
        TaskCreationOptions.LongRunning,
        TaskScheduler.Current
      )
      .Wait();

    return token;
  }

  public SubscriptionToken SubscribePeriodic(
    TimeSpan period,
    Action<object> action,
    ThreadOption threadOption = ThreadOption.PublisherThread
  )
  {
    var token = Subscribe(action, threadOption, EventFeatures.None);
    Task.Factory.StartNew(
        async () =>
        {
          while (token.IsActive)
          {
            await Task.Delay(period);
            await InternalPublish(new object());
          }
        },
        default,
        TaskCreationOptions.LongRunning,
        TaskScheduler.Current
      )
      .Wait();

    return token;
  }
}
