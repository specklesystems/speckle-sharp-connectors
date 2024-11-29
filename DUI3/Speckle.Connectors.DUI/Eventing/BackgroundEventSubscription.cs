using Speckle.Connectors.Common.Threading;

namespace Speckle.Connectors.DUI.Eventing;

public class BackgroundEventSubscription<TPayload>(
  IDelegateReference actionReference,
  IDelegateReference filterReference,
  IThreadContext threadContext
) : EventSubscription<TPayload>(actionReference, filterReference)
{
  public override void InvokeAction(Action<TPayload> action, TPayload argument) =>
    threadContext.RunOnWorker(() => action(argument));
}
