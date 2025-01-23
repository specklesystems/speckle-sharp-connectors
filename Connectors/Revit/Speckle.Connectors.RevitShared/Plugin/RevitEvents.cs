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

public static class RevitEvents
{
#if REVIT2022
  private static readonly System.Timers.Timer s_selectionTimer = new(1000);
#else
  private static IEventAggregator? s_eventAggregator;
#endif

  public static void Register(IEventAggregator eventAggregator, UIControlledApplication application)
  {
#if !REVIT2022
    s_eventAggregator = eventAggregator;
#endif
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
    s_selectionTimer.Elapsed += async (_, _) =>
      await eventAggregator.GetEvent<SelectionChangedEvent>().PublishAsync(new object());
    s_selectionTimer.Start();
#else

    application.SelectionChanged += (_, _) =>
      eventAggregator.GetEvent<IdleEvent>().OneTimeSubscribe("Selection", OnSelectionChanged);
#endif
  }

#if !REVIT2022
  private static async Task OnSelectionChanged(object _)
  {
    if (s_eventAggregator is null)
    {
      return;
    }
    await s_eventAggregator.GetEvent<SelectionChangedEvent>().PublishAsync(new object());
  }
#endif
}
