using Rhino;
using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.Rhino.HostApp;

/// <summary>
/// Rhino Idle Manager is a helper util to manage deferred actions.
/// </summary>
public sealed class RhinoIdleManager(IIdleCallManager idleCallManager) : AppIdleManager(idleCallManager)
{
  private readonly IIdleCallManager _idleCallManager = idleCallManager;

  protected override void AddEvent()
  {
    RhinoApp.Idle += RhinoAppOnIdle;
  }

  private void RhinoAppOnIdle(object? sender, EventArgs e) =>
    _idleCallManager.AppOnIdle(() => RhinoApp.Idle -= RhinoAppOnIdle);
}
