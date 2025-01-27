using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Eventing;

namespace Speckle.Connector.Navisworks
{
  
  public class SelectionChangedEvent(IThreadContext threadContext, ITopLevelExceptionHandler exceptionHandler)
    : ThreadedEvent<object>(threadContext, exceptionHandler);
  public static class NavisworksEvents
  {
    public static void Register(IEventAggregator eventAggregator)
    {
      NavisworksApp.Idle += async (_, _) => await eventAggregator.GetEvent<IdleEvent>().PublishAsync(new object());
      NavisworksApp.ActiveDocument.CurrentSelection.Changed += async (_, _) =>
        await eventAggregator.GetEvent<SelectionChangedEvent>().PublishAsync(new object());
    }
  }
}
