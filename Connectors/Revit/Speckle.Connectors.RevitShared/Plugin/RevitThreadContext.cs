using Revit.Async;
using Speckle.Connectors.Common.Threading;

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

  private static async Task<T> CatchExceptions<T>(Func<T> action)
  {
    Exception? ex = null;
    //force the usage of the application overload
    var ret = await RevitTask.RunAsync(_ =>
    {
      try
      {
        return action();
      }
#pragma warning disable CA1031
      catch (Exception e)
#pragma warning restore CA1031
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
    var ret = await RevitTask.RunAsync(async _ =>
    {
      try
      {
        return await action();
      }
#pragma warning disable CA1031
      catch (Exception e)
#pragma warning restore CA1031
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
    await RevitTask.RunAsync(async _ =>
    {
      try
      {
        await action();
      }
#pragma warning disable CA1031
      catch (Exception e)
#pragma warning restore CA1031
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
