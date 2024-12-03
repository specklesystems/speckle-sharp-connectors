using ArcGIS.Desktop.Framework.Threading.Tasks;
using Speckle.Connectors.Common.Threading;

namespace Speckle.Connectors.ArcGIS.Utils;

//don't check for GUI as it's the same check we do in ThreadContext
public class ArcGISThreadContext : ThreadContext
{
  protected override ValueTask<T> MainToWorkerAsync<T>(Func<ValueTask<T>> action)
  {
    if (QueuedTask.OnWorker)
    {
      return action();
    }
    else
    {
      return QueuedTask.Run(async() => await action().BackToCurrent()).AsValueTask();
    }
  }

  protected override ValueTask<T> WorkerToMainAsync<T>(Func<ValueTask<T>> action) => QueuedTask.Run(async() => await action().BackToCurrent()).AsValueTask();

  protected override ValueTask<T> MainToWorker<T>(Func<T> action)
  {
    if (QueuedTask.OnWorker)
    {
      return new(action());
    }
    else
    {
      return QueuedTask.Run(action).AsValueTask();
    }
  }

  protected override ValueTask<T> WorkerToMain<T>(Func<T> action) => QueuedTask.Run(action).AsValueTask();
}
