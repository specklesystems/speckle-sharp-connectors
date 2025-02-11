using ArcGIS.Desktop.Framework.Threading.Tasks;
using Speckle.Connectors.Common.Threading;

namespace Speckle.Connectors.ArcGIS.Utils;

//don't check for GUI as it's the same check we do in ThreadContext
public class ArcGISThreadContext : DefaultThreadContext
{
  public override Task AccessData(Action action)
  {
    if (QueuedTask.OnWorker)
    {
      return base.AccessData(action);
    }
    else
    {
      return QueuedTask.Run(action);
    }
  }

  public override Task<T> AccessDataAsync<T>(Func<T> action)
  {
    if (QueuedTask.OnWorker)
    {
      return base.AccessDataAsync(action);
    }
    else
    {
      return QueuedTask.Run(action);
    }
  }

  public override Task AccessDataAsync(Func<Task> action)
  {
    if (QueuedTask.OnWorker)
    {
      return base.AccessDataAsync(action);
    }
    else
    {
      return QueuedTask.Run(action);
    }
  }

  public override Task<T> AccessDataAsync<T>(Func<Task<T>> action)
  {
    if (QueuedTask.OnWorker)
    {
      return base.AccessDataAsync(action);
    }
    else
    {
      return QueuedTask.Run(action);
    }
  }
}
