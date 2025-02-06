using Speckle.Connectors.DUI.Bridge;
using Speckle.InterfaceGenerator;

namespace Speckle.Connectors.Autocad.HostApp;

public partial interface IAutocadIdleManager : IAppIdleManager;

[GenerateAutoInterface]
public sealed class AutocadIdleManager(IIdleCallManager idleCallManager)
  : AppIdleManager(idleCallManager),
    IAutocadIdleManager
{
  private readonly IIdleCallManager _idleCallManager = idleCallManager;

  protected override void AddEvent()
  {
    Application.Idle += AutocadAppOnIdle;
  }

  private void AutocadAppOnIdle(object? sender, EventArgs e) =>
    _idleCallManager.AppOnIdle(() => Application.Idle -= AutocadAppOnIdle);
}
