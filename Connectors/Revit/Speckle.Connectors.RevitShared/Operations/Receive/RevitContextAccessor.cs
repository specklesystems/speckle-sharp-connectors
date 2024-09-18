using Revit.Async;
using Speckle.Connectors.Common.Operations;

namespace Speckle.Connectors.Revit.Operations.Receive;

internal sealed class RevitContextAccessor : ISyncToThread
{
  public Task<T> RunOnThread<T>(Func<T> func) => RevitTask.RunAsync(func);
}
