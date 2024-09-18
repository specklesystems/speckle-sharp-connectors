using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.Autocad.HostApp;

public sealed class AutocadIdleManager(IIdleCallManager idleCallManager) : AppIdleManager(idleCallManager)
{
  private readonly IIdleCallManager _idleCallManager = idleCallManager;

  protected override void AddEvent()
  {
    Application.Idle += AutocadAppOnIdle;
  }

  private void AutocadAppOnIdle(object? sender, EventArgs e) =>
    _idleCallManager.AppOnIdle(() => Application.Idle -= AutocadAppOnIdle);
}
