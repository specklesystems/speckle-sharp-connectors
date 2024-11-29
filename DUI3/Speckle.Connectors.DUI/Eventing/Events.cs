using Speckle.Connectors.Common.Threading;

namespace Speckle.Connectors.DUI.Eventing;

public class ExceptionEvent(IThreadContext threadContext) : ThreadedEvent<Exception>(threadContext);
