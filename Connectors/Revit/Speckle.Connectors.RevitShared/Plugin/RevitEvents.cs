using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Eventing;

namespace Speckle.Connectors.Revit.Plugin;

public class ApplicationInitializedEvent(IThreadContext threadContext, ITopLevelExceptionHandler exceptionHandler)
  : ThreadedEvent<UIApplication>(threadContext, exceptionHandler);

public class ViewActivatedEvent(IThreadContext threadContext, ITopLevelExceptionHandler exceptionHandler)
  : ThreadedEvent<ViewActivatedEventArgs>(threadContext, exceptionHandler);

public class DocumentOpeningEvent(IThreadContext threadContext, ITopLevelExceptionHandler exceptionHandler)
  : ThreadedEvent<object>(threadContext, exceptionHandler);

public class DocumentOpenedEvent(IThreadContext threadContext, ITopLevelExceptionHandler exceptionHandler)
  : ThreadedEvent<object>(threadContext, exceptionHandler);

public class SelectionChangedEvent(IThreadContext threadContext, ITopLevelExceptionHandler exceptionHandler)
  : ThreadedEvent<object>(threadContext, exceptionHandler);

public class DocumentChangedEvent(IThreadContext threadContext, ITopLevelExceptionHandler exceptionHandler)
  : ThreadedEvent<Autodesk.Revit.DB.Events.DocumentChangedEventArgs>(threadContext, exceptionHandler);

#if REVIT2022
public class PeriodicSelectionEvent(IThreadContext threadContext, ITopLevelExceptionHandler exceptionHandler)
  : PeriodicThreadedEvent(threadContext, exceptionHandler);
#endif

public static class RevitEvents
{
  private static IEventAggregator? s_eventAggregator;

  public static void Register(IEventAggregator eventAggregator, UIControlledApplication application)
  {
    s_eventAggregator = eventAggregator;
    application.Idling += async (_, _) => await eventAggregator.GetEvent<IdleEvent>().PublishAsync(new object());
    application.ControlledApplication.ApplicationInitialized += async (sender, _) =>
      await eventAggregator
        .GetEvent<ApplicationInitializedEvent>()
        .PublishAsync(new UIApplication(sender as Application));
    application.ViewActivated += async (_, args) =>
      await eventAggregator.GetEvent<ViewActivatedEvent>().PublishAsync(args);
    application.ControlledApplication.DocumentOpened += async (_, _) =>
      await eventAggregator.GetEvent<DocumentOpenedEvent>().PublishAsync(new object());
    application.ControlledApplication.DocumentOpening += async (_, _) =>
      await eventAggregator.GetEvent<DocumentOpeningEvent>().PublishAsync(new object());
    application.ControlledApplication.DocumentChanged += async (_, args) =>
      await eventAggregator.GetEvent<DocumentChangedEvent>().PublishAsync(args);

#if REVIT2022
    // NOTE: getting the selection data should be a fast function all, even for '000s of elements - and having a timer hitting it every 1s is ok.
    eventAggregator.GetEvent<PeriodicSelectionEvent>().SubscribePeriodic(TimeSpan.FromSeconds(1), OnSelectionChanged);
#else

    application.SelectionChanged += (_, _) =>
      eventAggregator.GetEvent<IdleEvent>().OneTimeSubscribe("Selection", OnSelectionChanged);
#endif
  }

  private static async Task OnSelectionChanged(object _)
  {
    if (s_eventAggregator is null)
    {
      return;
    }
    await s_eventAggregator.GetEvent<SelectionChangedEvent>().PublishAsync(new object());
  }
}
