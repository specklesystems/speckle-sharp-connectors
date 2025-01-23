using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Eventing;

namespace Speckle.Connectors.Autocad.Plugin;


public class DocumentActivatedEvent(IThreadContext threadContext, ITopLevelExceptionHandler exceptionHandler)
  : ThreadedEvent<DocumentCollectionEventArgs>(threadContext, exceptionHandler);

public class DocumentToBeDestroyedEvent(IThreadContext threadContext, ITopLevelExceptionHandler exceptionHandler)
  : ThreadedEvent<DocumentCollectionEventArgs>(threadContext, exceptionHandler);
public class ImpliedSelectionChangedEvent(IThreadContext threadContext, ITopLevelExceptionHandler exceptionHandler)
  : ThreadedEvent<EventArgs>(threadContext, exceptionHandler);

public class ObjectAppendedEvent(IThreadContext threadContext, ITopLevelExceptionHandler exceptionHandler)
  : ThreadedEvent<ObjectEventArgs>(threadContext, exceptionHandler);
public class ObjectErasedEvent(IThreadContext threadContext, ITopLevelExceptionHandler exceptionHandler)
  : ThreadedEvent<ObjectErasedEventArgs>(threadContext, exceptionHandler);
public class ObjectModifiedEvent(IThreadContext threadContext, ITopLevelExceptionHandler exceptionHandler)
  : ThreadedEvent<ObjectEventArgs>(threadContext, exceptionHandler);

public  static class AutocadEvents
{
  public static void Register(IEventAggregator eventAggregator)
  {
    Application.Idle += async (_, e) =>
      await eventAggregator.GetEvent<IdleEvent>().PublishAsync(e);
    
    Application.DocumentManager.DocumentActivated += async (_, e) =>
      await eventAggregator.GetEvent<DocumentActivatedEvent>().PublishAsync(e);
    Application.DocumentManager.DocumentToBeDestroyed+= async (_, e) =>
      await eventAggregator.GetEvent<DocumentToBeDestroyedEvent>().PublishAsync(e);
  }
}
