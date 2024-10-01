using Rhino;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.Rhino.Plugin;
using Speckle.InterfaceGenerator;

namespace Speckle.Connectors.Rhino.DependencyInjection;

[GenerateAutoInterface]
public class RhinoPlugin : IRhinoPlugin
{
  private readonly IAppIdleManager _idleManager;

  public RhinoPlugin(IAppIdleManager idleManager)
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
