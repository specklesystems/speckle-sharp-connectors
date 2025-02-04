using Revit.Async;
using Speckle.Connectors.Common.Threading;

namespace Speckle.Connectors.Revit.Plugin;

public class RevitThreadContext : ThreadContext
{
  protected override Task<T> MainToWorkerAsync<T>(Func<Task<T>> action) => action();

  protected override Task<T> WorkerToMainAsync<T>(Func<Task<T>> action) =>
    RevitTask.RunAsync(async () => await action());

  protected override Task<T> MainToWorker<T>(Func<T> action) => Task.FromResult(action());

  protected override Task<T> WorkerToMain<T>(Func<T> action) => RevitTask.RunAsync(action);

  protected override Task RunMainAsync(Func<Task> action) => RevitTask.RunAsync(action);

  protected override Task<T> RunMainAsync<T>(Func<T> action) => RevitTask.RunAsync(action);

  protected override Task<T> RunMainAsync<T>(Func<Task<T>> action) => RevitTask.RunAsync(action);

  private static readonly AsyncLocal<bool> s_isInContext = new();

  public static Task Run(Func<Task> action)
  {
    if (s_isInContext.Value)
    {
      return action();
    }
    try
    {
      s_isInContext.Value = true;
      return RevitTask.RunAsync(action);
    }
    finally
    {
      s_isInContext.Value = false;
    }
  }

  public static Task Run(Action action)
  {
    if (s_isInContext.Value)
    {
      action();
      return Task.CompletedTask;
    }

    try
    {
      s_isInContext.Value = true;
      return RevitTask.RunAsync(action);
    }
    finally
    {
      s_isInContext.Value = false;
    }
  }

  public static Task<T> Run<T>(Func<T> action)
  {
    if (s_isInContext.Value)
    {
      var x = action();
      return Task.FromResult(x);
    }

    try
    {
      s_isInContext.Value = true;
      return RevitTask.RunAsync(action);
    }
    finally
    {
      s_isInContext.Value = false;
    }
  }
}
