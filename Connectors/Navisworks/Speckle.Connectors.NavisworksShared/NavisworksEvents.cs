using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Eventing;

namespace Speckle.Connector.Navisworks;

public class SelectionChangedEvent(IThreadContext threadContext, ITopLevelExceptionHandler exceptionHandler)
  : ThreadedEvent<object>(threadContext, exceptionHandler);
public class ActiveDocumentChangingEvent(IThreadContext threadContext, ITopLevelExceptionHandler exceptionHandler)
  : ThreadedEvent<object>(threadContext, exceptionHandler);
public class ActiveDocumentChangedEvent(IThreadContext threadContext, ITopLevelExceptionHandler exceptionHandler)
  : ThreadedEvent<object>(threadContext, exceptionHandler);
public class CollectionChangingEvent(IThreadContext threadContext, ITopLevelExceptionHandler exceptionHandler)
  : ThreadedEvent<object>(threadContext, exceptionHandler);
public class CollectionChangedEvent(IThreadContext threadContext, ITopLevelExceptionHandler exceptionHandler)
  : ThreadedEvent<object>(threadContext, exceptionHandler);
public static class NavisworksEvents
{
  public static void Register(IEventAggregator eventAggregator)
  {
    NavisworksApp.Idle += async (_, _) => await eventAggregator.GetEvent<IdleEvent>().PublishAsync(new object());
    NavisworksApp.ActiveDocument.CurrentSelection.Changed += async (_, _) =>
      await eventAggregator.GetEvent<SelectionChangedEvent>().PublishAsync(new object());
    NavisworksApp.ActiveDocumentChanging += async (_, _) =>
      await eventAggregator.GetEvent<ActiveDocumentChangingEvent>().PublishAsync(new object());
    NavisworksApp.ActiveDocumentChanged += async (_, _) =>
      await eventAggregator.GetEvent<ActiveDocumentChangedEvent>().PublishAsync(new object());
  }
}
