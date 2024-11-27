using ArcGIS.Desktop.Framework.Threading.Tasks;
using Speckle.Connectors.Common.Threading;

namespace Speckle.Connectors.ArcGIS.Utils;

public class ArcGISThreadContext : ThreadContext
{
  public override void RunOnThread(Action action, bool useMain)
  {
    if (useMain)
    {
      if (IsMainThread)
      {
        base.RunOnThread(action, useMain);
      } 
      else {
        QueuedTask.Run(action);
      }
    }
    else
    {
      if (IsMainThread)
      {
        QueuedTask.Run(action);
      } 
      else {
        base.RunOnThread(action, useMain);
      }
    }
  }

  public override async Task<T> RunOnThread<T>(Func<T> action, bool useMain)
  {
    if (useMain)
    {if (IsMainThread)
      {
        return await base.RunOnThread(action, useMain).BackToThread();
      }
      else
      {
        return await QueuedTask.Run(action).BackToThread();
      }
    }
    else
    {
      if (IsMainThread)
      {
        return await QueuedTask.Run(action).BackToThread();
      } 
      else {
        return await base.RunOnThread(action, useMain).BackToThread();
      }
    }
  }

  public override async Task RunOnThreadAsync(Func<Task> action, bool useMain) 
  {
    if (useMain)
    {
      if (IsMainThread)
      {
        await base.RunOnThreadAsync(action, useMain).BackToThread();
      }
      else
      {
        await QueuedTask.Run(action).BackToThread();
      }
    }
    else
    {
      if (IsMainThread)
      {
        await QueuedTask.Run(action).BackToThread();
      }
      else
      {
        await base.RunOnThreadAsync(action, useMain).BackToThread();
      }
    }
  }

  public override async Task<T> RunOnThreadAsync<T>(Func<Task<T>> action, bool useMain)

  {
    if (useMain)
    {
      if (IsMainThread)
      {
        return await base.RunOnThreadAsync(action, useMain).BackToThread();
      }
      else
      {
        return await QueuedTask.Run(action).BackToThread();
      }
    }
    else
    {
      if (IsMainThread)
      {
        return await QueuedTask.Run(action).BackToThread();
      }
      else
      {
        return await base.RunOnThreadAsync(action, useMain).BackToThread();
      }
    }
  }
}
