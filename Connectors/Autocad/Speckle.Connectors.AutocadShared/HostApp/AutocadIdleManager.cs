using Speckle.Connectors.DUI.Bridge;
using Speckle.InterfaceGenerator;

namespace Speckle.Connectors.Autocad.HostApp;

[GenerateAutoInterface]
public class AutocadIdleManager(IIdleCallManager idleCallManager) : IAutocadIdleManager
{
  /// <summary>
  /// Subscribe deferred action to AutocadIdle event to run it whenever Autocad become idle.
  /// </summary>
  /// <param name="action"> Action to call whenever Autocad become Idle.</param>
  public void SubscribeToIdle(string id, Action action)
  {
    if (idleCallManager.TrySubscribeToIdle(id, action))
    {
      Application.Idle += RhinoAppOnIdle;
    }
  }

  private void RhinoAppOnIdle(object sender, EventArgs e) =>
    idleCallManager.AppOnIdle(() => Application.Idle -= RhinoAppOnIdle);
}
