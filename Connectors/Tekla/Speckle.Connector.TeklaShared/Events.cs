using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Eventing;

namespace Speckle.Connectors.RhinoShared;

public class SelectionChange(IThreadContext threadContext, ITopLevelExceptionHandler exceptionHandler)
  : ThreadedEvent<object>(threadContext, exceptionHandler);

public class ModelObjectChanged(IThreadContext threadContext, ITopLevelExceptionHandler exceptionHandler)
  : ThreadedEvent<List<TSM.ChangeData>>(threadContext, exceptionHandler);

public class ModelLoad(IThreadContext threadContext, ITopLevelExceptionHandler exceptionHandler)
  : ThreadedEvent<object>(threadContext, exceptionHandler);

public static class TeklaEvents
{
  public static void Register(Tekla.Structures.Model.Events events, IEventAggregator eventAggregator)
  {
    events.UnRegister();
    events.ModelSave += () => eventAggregator.GetEvent<IdleEvent>().Publish(new object());
    events.SelectionChange += () => eventAggregator.GetEvent<SelectionChange>().Publish(new object());
    events.ModelObjectChanged += x => eventAggregator.GetEvent<ModelObjectChanged>().Publish(x);
    events.ModelLoad += () => eventAggregator.GetEvent<ModelLoad>().Publish(new object());
    events.Register();
  }
}
