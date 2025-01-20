using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Eventing;

namespace Speckle.Connectors.RhinoShared;

public class SelectionChangeEvent(IThreadContext threadContext, ITopLevelExceptionHandler exceptionHandler)
  : ThreadedEvent<object>(threadContext, exceptionHandler);

public class ModelObjectChangedEvent(IThreadContext threadContext, ITopLevelExceptionHandler exceptionHandler)
  : ThreadedEvent<List<TSM.ChangeData>>(threadContext, exceptionHandler);

public class ModelLoadEvent(IThreadContext threadContext, ITopLevelExceptionHandler exceptionHandler)
  : ThreadedEvent<object>(threadContext, exceptionHandler);

public static class TeklaEvents
{
  public static void Register(Tekla.Structures.Model.Events events, IEventAggregator eventAggregator)
  {
    events.UnRegister();
    events.SelectionChange += () => eventAggregator.GetEvent<SelectionChangeEvent>().Publish(new object());
    events.ModelObjectChanged += x => eventAggregator.GetEvent<ModelObjectChangedEvent>().Publish(x);
    events.ModelLoad += () => eventAggregator.GetEvent<ModelLoadEvent>().Publish(new object());
    events.Register();
  }
}
