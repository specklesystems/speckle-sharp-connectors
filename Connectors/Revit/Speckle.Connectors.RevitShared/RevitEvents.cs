using Revit.Async;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bridge;
using Speckle.InterfaceGenerator;

namespace Speckle.Connectors.RevitShared;

[GenerateAutoInterface]
public class RevitEvents(ITopLevelExceptionHandler topLevelExceptionHandler) : IRevitEvents
{
  public void Add(Func<Task> handler) =>
    RevitTask.RunAsync(() => topLevelExceptionHandler.FireAndForget(handler)).FireAndForget();

  public void Add(Action handler) =>
    RevitTask.RunAsync(() => topLevelExceptionHandler.CatchUnhandled(handler)).FireAndForget();
}
