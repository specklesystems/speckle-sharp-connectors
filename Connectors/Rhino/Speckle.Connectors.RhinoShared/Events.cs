using Rhino;
using Rhino.DocObjects;
using Rhino.DocObjects.Tables;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Eventing;

namespace Speckle.Connectors.RhinoShared;

public class BeginOpenDocument(IThreadContext threadContext, ITopLevelExceptionHandler exceptionHandler)
  : ThreadedEvent<DocumentOpenEventArgs>(threadContext, exceptionHandler);

public class EndOpenDocument(IThreadContext threadContext, ITopLevelExceptionHandler exceptionHandler)
  : ThreadedEvent<DocumentOpenEventArgs>(threadContext, exceptionHandler);

public class SelectObjects(IThreadContext threadContext, ITopLevelExceptionHandler exceptionHandler)
  : ThreadedEvent<RhinoObjectSelectionEventArgs>(threadContext, exceptionHandler);

public class DeselectObjects(IThreadContext threadContext, ITopLevelExceptionHandler exceptionHandler)
  : ThreadedEvent<RhinoObjectSelectionEventArgs>(threadContext, exceptionHandler);

public class DeselectAllObjects(IThreadContext threadContext, ITopLevelExceptionHandler exceptionHandler)
  : ThreadedEvent<RhinoDeselectAllObjectsEventArgs>(threadContext, exceptionHandler);

public class ActiveDocumentChanged(IThreadContext threadContext, ITopLevelExceptionHandler exceptionHandler)
  : ThreadedEvent<DocumentEventArgs>(threadContext, exceptionHandler);

public class DocumentPropertiesChanged(IThreadContext threadContext, ITopLevelExceptionHandler exceptionHandler)
  : ThreadedEvent<DocumentEventArgs>(threadContext, exceptionHandler);

public class AddRhinoObject(IThreadContext threadContext, ITopLevelExceptionHandler exceptionHandler)
  : ThreadedEvent<RhinoObjectEventArgs>(threadContext, exceptionHandler);

public class DeleteRhinoObject(IThreadContext threadContext, ITopLevelExceptionHandler exceptionHandler)
  : ThreadedEvent<RhinoObjectEventArgs>(threadContext, exceptionHandler);

public class RenderMaterialsTableEvent(IThreadContext threadContext, ITopLevelExceptionHandler exceptionHandler)
  : ThreadedEvent<RhinoDoc.RenderContentTableEventArgs>(threadContext, exceptionHandler);

public class MaterialTableEvent(IThreadContext threadContext, ITopLevelExceptionHandler exceptionHandler)
  : ThreadedEvent<MaterialTableEventArgs>(threadContext, exceptionHandler);

public class ModifyObjectAttributes(IThreadContext threadContext, ITopLevelExceptionHandler exceptionHandler)
  : ThreadedEvent<RhinoModifyObjectAttributesEventArgs>(threadContext, exceptionHandler);

public class ReplaceRhinoObject(IThreadContext threadContext, ITopLevelExceptionHandler exceptionHandler)
  : ThreadedEvent<RhinoReplaceObjectEventArgs>(threadContext, exceptionHandler);

public static class RhinoEvents
{
  public static void Register(IEventAggregator eventAggregator)
  {
    RhinoApp.Idle += (_, e) => eventAggregator.GetEvent<IdleEvent>().Publish(e);

    RhinoDoc.BeginOpenDocument += (_, e) => eventAggregator.GetEvent<BeginOpenDocument>().Publish(e);
    RhinoDoc.EndOpenDocument += (_, e) => eventAggregator.GetEvent<EndOpenDocument>().Publish(e);
    RhinoDoc.SelectObjects += (_, e) => eventAggregator.GetEvent<SelectObjects>().Publish(e);
    RhinoDoc.DeselectObjects += (_, e) => eventAggregator.GetEvent<DeselectObjects>().Publish(e);
    RhinoDoc.DeselectAllObjects += (_, e) => eventAggregator.GetEvent<DeselectAllObjects>().Publish(e);
    RhinoDoc.ActiveDocumentChanged += (_, e) => eventAggregator.GetEvent<ActiveDocumentChanged>().Publish(e);
    RhinoDoc.DocumentPropertiesChanged += (_, e) => eventAggregator.GetEvent<DocumentPropertiesChanged>().Publish(e);
    RhinoDoc.AddRhinoObject += (_, e) => eventAggregator.GetEvent<AddRhinoObject>().Publish(e);
    RhinoDoc.DeleteRhinoObject += (_, e) => eventAggregator.GetEvent<DeleteRhinoObject>().Publish(e);
    RhinoDoc.RenderMaterialsTableEvent += (_, e) => eventAggregator.GetEvent<RenderMaterialsTableEvent>().Publish(e);
    RhinoDoc.MaterialTableEvent += (_, e) => eventAggregator.GetEvent<MaterialTableEvent>().Publish(e);
    RhinoDoc.ModifyObjectAttributes += (_, e) => eventAggregator.GetEvent<ModifyObjectAttributes>().Publish(e);
    RhinoDoc.ReplaceRhinoObject += (_, e) => eventAggregator.GetEvent<ReplaceRhinoObject>().Publish(e);
  }
}
