using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.Revit.Common;
using Speckle.InterfaceGenerator;

namespace Speckle.Connectors.Revit.Plugin;

[GenerateAutoInterface]
public class RevitTask(ITopLevelExceptionHandler topLevelExceptionHandler) : IRevitTask
{
  public void Run(Func<Task> handler) =>
    RevitAsync.RunAsync(() => topLevelExceptionHandler.FireAndForget(handler)).FireAndForget();

  public void Run(Action handler) =>
    RevitAsync.RunAsync(() => topLevelExceptionHandler.CatchUnhandled(handler)).FireAndForget();

  public Task RunAsync(Func<Task> handler) =>
    RevitAsync.RunAsync(() => topLevelExceptionHandler.CatchUnhandledAsync(handler));

  public Task RunAsync(Action handler) =>
    RevitAsync.RunAsync(() => topLevelExceptionHandler.CatchUnhandled(handler));
}
