using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.Revit.Common;
using Speckle.Sdk;

namespace Speckle.Connectors.Revit.Plugin;

public class RevitThreadContext : ThreadContext
{
  protected override Task<T> MainToWorkerAsync<T>(Func<Task<T>> action) => action();

  protected override Task<T> WorkerToMainAsync<T>(Func<Task<T>> action) => CatchExceptions(action);

  protected override Task<T> MainToWorker<T>(Func<T> action) => Task.FromResult(action());

  protected override Task<T> WorkerToMain<T>(Func<T> action) => CatchExceptions(action);

  protected override Task RunMainAsync(Func<Task> action) => CatchExceptions(action);

  protected override Task<T> RunMainAsync<T>(Func<T> action) => CatchExceptions(action);

  protected override Task<T> RunMainAsync<T>(Func<Task<T>> action) => CatchExceptions(action);

  protected override Task RunMain(Action action) => CatchExceptions(action);

  private static async Task<T> CatchExceptions<T>(Func<T> action)
  {
    Exception? ex = null;
    //force the usage of the application overload
    var ret = await RevitAsync.RunAsync(() =>
    {
      try
      {
        return action();
      }
      catch (Exception e) when (!e.IsFatal())
      {
        ex = e;
        return default;
      }
    });
    if (ex is not null)
    {
      throw new SpeckleRevitTaskException(ex);
    }
    return ret!;
  }

  private static async Task<T> CatchExceptions<T>(Func<Task<T>> action)
  {
    Exception? ex = null;
    //force the usage of the application overload
    var ret = await RevitAsync.RunAsync(async () =>
    {
      try
      {
        return await action();
      }
      catch (Exception e) when (!e.IsFatal())
      {
        ex = e;
        return default;
      }
    });
    if (ex is not null)
    {
      throw new SpeckleRevitTaskException(ex);
    }
    return ret!;
  }

  private static async Task CatchExceptions(Func<Task> action)
  {
    Exception? ex = null;
    //force the usage of the application overload
    await RevitAsync.RunAsync(async () =>
    {
      try
      {
        await action();
      }
      catch (Exception e) when (!e.IsFatal())
      {
        ex = e;
      }
    });
    if (ex is not null)
    {
      throw new SpeckleRevitTaskException(ex);
    }
  }

  private static async Task CatchExceptions(Action action)
  {
    Exception? ex = null;
    //force the usage of the application overload
    await RevitAsync.RunAsync(() =>
    {
      try
      {
        action();
      }
      catch (Exception e) when (!e.IsFatal())
      {
        ex = e;
      }
    });
    if (ex is not null)
    {
      throw new SpeckleRevitTaskException(ex);
    }
  }
}
