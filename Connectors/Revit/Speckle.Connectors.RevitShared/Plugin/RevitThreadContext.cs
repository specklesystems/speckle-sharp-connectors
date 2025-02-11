using Revit.Async;
using Speckle.Connectors.Common.Threading;

namespace Speckle.Connectors.Revit.Plugin;

public class RevitThreadContext : DefaultThreadContext
{
  public override Task AccessData(Action action) => RevitTask.RunAsync(action);

  public override Task<T> AccessDataAsync<T>(Func<T> action) => RevitTask.RunAsync(action);

  public override Task AccessDataAsync(Func<Task> action) => RevitTask.RunAsync(action);

  public override Task<T> AccessDataAsync<T>(Func<Task<T>> action) => RevitTask.RunAsync(action);
}
