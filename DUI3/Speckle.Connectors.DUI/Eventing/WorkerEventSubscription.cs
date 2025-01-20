using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.DUI.Eventing;

public class WorkerEventSubscriptionAsync<T>(
  IDelegateReference actionReference,
  IThreadContext threadContext,
  ITopLevelExceptionHandler exceptionHandler,
  SubscriptionToken token,
  bool isOnce
) : OneTimeEventSubscriptionAsync<T>(actionReference, exceptionHandler, token, isOnce)
{
  public override Task InvokeAction(Func<T, Task> action, T payload) =>
    threadContext.RunOnWorkerAsync(() => action.Invoke(payload));
}

public class WorkerEventSubscriptionSync<T>(
  IDelegateReference actionReference,
  IThreadContext threadContext,
  ITopLevelExceptionHandler exceptionHandler,
  SubscriptionToken token,
  bool isOnce
) : OneTimeEventSubscriptionSync<T>(actionReference, exceptionHandler, token, isOnce)
{
  public override Task InvokeAction(Action<T> action, T payload) =>
    threadContext.RunOnWorker(() => action.Invoke(payload));
}
