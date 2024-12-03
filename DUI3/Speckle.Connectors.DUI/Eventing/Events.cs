using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.DUI.Eventing;

public class ExceptionEvent(IThreadContext threadContext, ITopLevelExceptionHandler exceptionHandler) : ThreadedEvent<Exception>(threadContext, exceptionHandler);

public class DocumentChangedEvent(IThreadContext threadContext, ITopLevelExceptionHandler exceptionHandler) 
  : ThreadedEvent<object>(threadContext, exceptionHandler);
