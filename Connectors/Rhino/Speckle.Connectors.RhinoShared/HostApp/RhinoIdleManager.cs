using Rhino;
using Speckle.Connectors.DUI.Bridge;
using Speckle.InterfaceGenerator;

namespace Speckle.Connectors.Rhino.HostApp;

/// <summary>
/// Rhino Idle Manager is a helper util to manage deferred actions.
/// </summary>
[GenerateAutoInterface]
public class RhinoIdleManager(IIdleCallManager idleCallManager) : IRhinoIdleManager
{
  /// <summary>
  /// Subscribe deferred action to RhinoIdle event to run it whenever Rhino become idle.
  /// </summary>
  /// <param name="action"> Action to call whenever Rhino become Idle.</param>
  public void SubscribeToIdle(string id, Action action) =>
    idleCallManager.SubscribeToIdle(
      id,
      action,
      () =>
      {
        RhinoApp.Idle += RhinoAppOnIdle;
      }
    );

  private void RhinoAppOnIdle(object? sender, EventArgs e) =>
    idleCallManager.AppOnIdle(() => RhinoApp.Idle -= RhinoAppOnIdle);
}