using ArcGIS.Desktop.Framework.Threading.Tasks;
using Speckle.Connectors.Common.Threading;

namespace Speckle.Connectors.ArcGIS.Utils;

//don't check for GUI as it's the same check we do in ThreadContext
public class ArcGISThreadContext : ThreadContext
{
  protected override Task<T> MainToWorkerAsync<T>(Func<Task<T>> action)
  {
    if (QueuedTask.OnWorker)
    {
      return action();
    }
    else
    {
      return QueuedTask.Run(action);
    }
  }

  protected override Task<T> WorkerToMainAsync<T>(Func<Task<T>> action) => QueuedTask.Run(action);

  protected override Task<T> MainToWorker<T>(Func<T> action)   
  {
    if (QueuedTask.OnWorker)
    {
      return Task.FromResult(action());
    }
    else
    {
      return QueuedTask.Run(action);
    }
  }

  protected override Task<T> WorkerToMain<T>(Func<T> action)   => QueuedTask.Run(action);
}
