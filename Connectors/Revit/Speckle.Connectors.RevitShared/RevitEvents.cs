using Revit.Async;
using Speckle.Connectors.Common.Threading;
using Speckle.InterfaceGenerator;

namespace Speckle.Connectors.RevitShared;

[GenerateAutoInterface]
public class RevitEvents : IRevitEvents
{
  public void Add(Func<Task> handler) => RevitTask.RunAsync(handler).FireAndForget();

  public void Add(Action handler) => RevitTask.RunAsync(handler).FireAndForget();
}
