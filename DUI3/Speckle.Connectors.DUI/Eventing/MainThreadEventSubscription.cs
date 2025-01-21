using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.DUI.Eventing;

public class MainThreadEventSubscription<T>(
  DelegateReference actionReference,
  IThreadContext threadContext,
  ITopLevelExceptionHandler exceptionHandler,
  SubscriptionToken token,
  bool isOnce
) : OneTimeEventSubscription<T>(actionReference, exceptionHandler, token, isOnce)
  where T : notnull
{
  public override Task InvokeAction(T payload) => threadContext.RunOnMainAsync(() => base.InvokeAction(payload));
}
