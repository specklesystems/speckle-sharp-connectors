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
    StartPeriodic(token, period);
    return token;
  }

  public SubscriptionToken SubscribePeriodic(
    TimeSpan period,
    Action<object> action,
    ThreadOption threadOption = ThreadOption.PublisherThread
  )
  {
    var token = Subscribe(action, threadOption, EventFeatures.None);
    StartPeriodic(token, period);
    return token;
  }

  private void StartPeriodic(SubscriptionToken token, TimeSpan period) =>
    Task
      .Factory.StartNew(
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
}
