using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.DUI.Eventing;

public class WorkerEventSubscription<TPayload>(
  IDelegateReference actionReference,
  IDelegateReference filterReference,
  IThreadContext threadContext,
  ITopLevelExceptionHandler exceptionHandler,
  bool isOnce
) : OneTimeEventSubscription<TPayload>(actionReference, filterReference, exceptionHandler, isOnce)
{
  public override void InvokeAction(Action<TPayload> action, TPayload argument) =>
    threadContext.RunOnWorker(() => action(argument)).BackToCurrent();
}
