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

public class GroupTableEvent(IThreadContext threadContext, ITopLevelExceptionHandler exceptionHandler)
  : ThreadedEvent<GroupTableEventArgs>(threadContext, exceptionHandler);

public class LayerTableEvent(IThreadContext threadContext, ITopLevelExceptionHandler exceptionHandler)
  : ThreadedEvent<LayerTableEventArgs>(threadContext, exceptionHandler);

public static class RhinoEvents
{
  public static void Register(IEventAggregator eventAggregator)
  {
    RhinoApp.Idle += async (_, e) => await eventAggregator.GetEvent<IdleEvent>().PublishAsync(e);

    RhinoDoc.BeginOpenDocument += async (_, e) => await eventAggregator.GetEvent<BeginOpenDocument>().PublishAsync(e);
    RhinoDoc.EndOpenDocument += async (_, e) => await eventAggregator.GetEvent<EndOpenDocument>().PublishAsync(e);
    RhinoDoc.SelectObjects += async (_, e) => await eventAggregator.GetEvent<SelectObjects>().PublishAsync(e);
    RhinoDoc.DeselectObjects += async (_, e) => await eventAggregator.GetEvent<DeselectObjects>().PublishAsync(e);
    RhinoDoc.DeselectAllObjects += async (_, e) => await eventAggregator.GetEvent<DeselectAllObjects>().PublishAsync(e);
    RhinoDoc.ActiveDocumentChanged += async (_, e) =>
      await eventAggregator.GetEvent<ActiveDocumentChanged>().PublishAsync(e);
    RhinoDoc.DocumentPropertiesChanged += async (_, e) =>
      await eventAggregator.GetEvent<DocumentPropertiesChanged>().PublishAsync(e);
    RhinoDoc.AddRhinoObject += async (_, e) => await eventAggregator.GetEvent<AddRhinoObject>().PublishAsync(e);
    RhinoDoc.DeleteRhinoObject += async (_, e) => await eventAggregator.GetEvent<DeleteRhinoObject>().PublishAsync(e);
    RhinoDoc.RenderMaterialsTableEvent += async (_, e) =>
      await eventAggregator.GetEvent<RenderMaterialsTableEvent>().PublishAsync(e);
    RhinoDoc.MaterialTableEvent += async (_, e) => await eventAggregator.GetEvent<MaterialTableEvent>().PublishAsync(e);
    RhinoDoc.ModifyObjectAttributes += async (_, e) =>
      await eventAggregator.GetEvent<ModifyObjectAttributes>().PublishAsync(e);
    RhinoDoc.ReplaceRhinoObject += async (_, e) => await eventAggregator.GetEvent<ReplaceRhinoObject>().PublishAsync(e);
    RhinoDoc.GroupTableEvent += async (_, e) => await eventAggregator.GetEvent<GroupTableEvent>().PublishAsync(e);
    RhinoDoc.LayerTableEvent += async (_, e) => await eventAggregator.GetEvent<LayerTableEvent>().PublishAsync(e);
  }
}
