using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.CSiShared.HostApp;

public sealed class CSiSharedIdleManager : AppIdleManager
{
  private readonly IIdleCallManager _idleCallManager;

  public CSiSharedIdleManager(IIdleCallManager idleCallManager)
    : base(idleCallManager)
  {
    _idleCallManager = idleCallManager;
  }

  protected override void AddEvent()
  {
    // ETABS specific idle handling can be added here if needed
    _idleCallManager.AppOnIdle(() => { });
  }
}
