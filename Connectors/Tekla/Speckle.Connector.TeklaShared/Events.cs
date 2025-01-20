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
    events.SelectionChange += async () => await eventAggregator.GetEvent<SelectionChange>().PublishAsync(new object());
    events.ModelObjectChanged += async x => await eventAggregator.GetEvent<ModelObjectChanged>().PublishAsync(x);
    events.ModelLoad += async () => await eventAggregator.GetEvent<ModelLoad>().PublishAsync(new object());
    events.Register();
  }
}
