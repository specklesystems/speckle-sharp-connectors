using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.DUI.Eventing;

public class MainThreadEventSubscriptionAsync<T>(
  IDelegateReference actionReference,
  IThreadContext threadContext,
  ITopLevelExceptionHandler exceptionHandler,
  bool isOnce
) : OneTimeEventSubscriptionAsync<T>(actionReference, exceptionHandler, isOnce)
{
  public override Task InvokeAction(Func<T, Task> action, T payload) =>
    threadContext.RunOnMainAsync(() => action.Invoke(payload));
}

public class MainThreadEventSubscriptionSync<T>(
  IDelegateReference actionReference,
  IThreadContext threadContext,
  ITopLevelExceptionHandler exceptionHandler,
  bool isOnce
) : OneTimeEventSubscriptionSync<T>(actionReference, exceptionHandler, isOnce)
{
  public override Task InvokeAction(Action<T> action, T payload) =>
    threadContext.RunOnMain(() => action.Invoke(payload));
}
