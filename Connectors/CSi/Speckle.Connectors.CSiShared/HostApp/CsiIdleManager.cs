using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.CSiShared.HostApp;

public sealed class CsiIdleManager : AppIdleManager
{
  private readonly IIdleCallManager _idleCallManager;

  public CsiIdleManager(IIdleCallManager idleCallManager)
    : base(idleCallManager)
  {
    _idleCallManager = idleCallManager;
  }

  protected override void AddEvent()
  {
    // TODO: CSi specific idle handling can be added here if needed
    _idleCallManager.AppOnIdle(() => { });
  }
}
