using Rhino;
using Speckle.Connectors.Rhino.HostApp;
using Speckle.Connectors.Rhino.Plugin;
using Speckle.InterfaceGenerator;

namespace Speckle.Connectors.Rhino.DependencyInjection;

[GenerateAutoInterface]
public class RhinoPlugin : IRhinoPlugin
{
  private readonly IRhinoIdleManager _idleManager;

  public RhinoPlugin(IRhinoIdleManager idleManager)
  {
    _idleManager = idleManager;
  }

  public void Initialise() =>
    _idleManager.SubscribeToIdle(
      nameof(RhinoPlugin),
      () => RhinoApp.RunScript(SpeckleConnectorsRhinoCommand.Instance.EnglishName, false)
    );

  public void Shutdown() { }
}
