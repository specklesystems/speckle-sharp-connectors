using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Eventing;

namespace Speckle.Connectors.CSiShared.Events;

public class SelectionBindingEvent(IThreadContext threadContext, ITopLevelExceptionHandler exceptionHandler)
  : PeriodicThreadedEvent(threadContext, exceptionHandler);
