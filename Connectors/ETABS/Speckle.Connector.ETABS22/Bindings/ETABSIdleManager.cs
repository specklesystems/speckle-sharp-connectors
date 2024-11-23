using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connector.ETABS22.Bindings;

public sealed class EtabsIdleManager : AppIdleManager
{
  private readonly IIdleCallManager _idleCallManager;

  public EtabsIdleManager(IIdleCallManager idleCallManager)
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
