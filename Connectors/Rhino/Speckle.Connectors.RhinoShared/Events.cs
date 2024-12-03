using Rhino;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Eventing;

namespace Speckle.Connectors.RhinoShared;

public class BeginOpenDocument(IThreadContext threadContext, ITopLevelExceptionHandler exceptionHandler)
  : ThreadedEvent<DocumentOpenEventArgs>(threadContext, exceptionHandler);
public class EndOpenDocument(IThreadContext threadContext, ITopLevelExceptionHandler exceptionHandler)
  : ThreadedEvent<DocumentOpenEventArgs>(threadContext, exceptionHandler);

public static class RhinoEvents
{
  public static void Register(IEventAggregator eventAggregator)
  {
    RhinoDoc.BeginOpenDocument +=
      (_, e)  => eventAggregator.GetEvent<BeginOpenDocument>().Publish(e);
    RhinoDoc.EndOpenDocument +=
      (_, e)  => eventAggregator.GetEvent<EndOpenDocument>().Publish(e);
  }
}
