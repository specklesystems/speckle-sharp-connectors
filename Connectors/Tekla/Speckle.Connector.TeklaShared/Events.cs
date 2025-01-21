using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Eventing;

namespace Speckle.Connectors.TeklaShared;

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
    events.SelectionChange += async () =>
      await eventAggregator.GetEvent<SelectionChangeEvent>().PublishAsync(new object());
    events.ModelObjectChanged += async x => await eventAggregator.GetEvent<ModelObjectChangedEvent>().PublishAsync(x);
    events.ModelLoad += async () => await eventAggregator.GetEvent<ModelLoadEvent>().PublishAsync(new object());
    events.Register();
  }
}
