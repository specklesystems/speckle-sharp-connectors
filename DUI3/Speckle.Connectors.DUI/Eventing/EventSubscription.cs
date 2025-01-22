using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.DUI.Eventing;

public class EventSubscription<TPayload>(
  DelegateReference actionReference,
  IThreadContext threadContext,
  ITopLevelExceptionHandler exceptionHandler,
  SubscriptionToken token,
  ThreadOption threadOption,
  EventFeatures features
) : IEventSubscription
  where TPayload : notnull
{
  public SubscriptionToken SubscriptionToken => token;

  public Func<object[], Task>? GetExecutionStrategy() =>
    async arguments =>
    {
      TPayload argument = (TPayload)arguments[0];
      await InvokeAction(argument);
    };

  private async Task InvokeAction(TPayload argument)
  {
    switch (threadOption)
    {
      case ThreadOption.MainThread:
        await threadContext.RunOnMainAsync(() => Invoke(argument));
        break;
      case ThreadOption.WorkerThread:
        await threadContext.RunOnWorkerAsync(() => Invoke(argument));
        break;
      case ThreadOption.PublisherThread:
      default:
        await Invoke(argument);
        break;
    }
  }

  private async Task Invoke(TPayload argument)
  {
    await exceptionHandler.CatchUnhandledAsync(() => actionReference.Invoke(argument));
    if (features.HasFlag(EventFeatures.OneTime))
    {
      SubscriptionToken.Dispose();
    }
  }
}
