using ArcGIS.Desktop.Framework.Threading.Tasks;
using Speckle.Connectors.Common.Threading;

namespace Speckle.Connectors.ArcGIS.Utils;

public class ArcGISThreadContext : ThreadContext
{
  protected override Task<T> MainToWorkerAsync<T>(Func<Task<T>> action) => QueuedTask.Run(action);

  protected override Task<T> WorkerToMainAsync<T>(Func<Task<T>> action) => QueuedTask.Run(action);

  protected override Task<T> MainToWorker<T>(Func<T> action)  => QueuedTask.Run(action);

  protected override Task<T> WorkerToMain<T>(Func<T> action) => QueuedTask.Run(action);
}
