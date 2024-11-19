using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connector.Navisworks.HostApp;

public sealed class NavisworksIdleManager(IIdleCallManager idleCallManager) : AppIdleManager(idleCallManager)
{
  private readonly IIdleCallManager _idleCallManager = idleCallManager;

  protected override void AddEvent() => NavisworksApp.Idle += NavisworksAppOnIdle;

  private void NavisworksAppOnIdle(object? sender, EventArgs e) =>
    _idleCallManager.AppOnIdle(() => NavisworksApp.Idle -= NavisworksAppOnIdle);
}
