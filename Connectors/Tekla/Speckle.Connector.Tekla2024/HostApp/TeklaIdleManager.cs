using Speckle.Connectors.DUI.Bridge;
using Tekla.Structures.Model;

namespace Speckle.Connector.Tekla2024.HostApp;

public sealed class TeklaIdleManager : AppIdleManager
{
  private readonly IIdleCallManager _idleCallManager;
  private readonly Events _events;

  public TeklaIdleManager(IIdleCallManager idleCallManager) : base(idleCallManager)
  {
    _idleCallManager = idleCallManager;
    _events = new Events();
  }

  protected override void AddEvent()
  {
    _events.ModelSave += TeklaEventsOnIdle;
    _events.Register();
  }

  private void TeklaEventsOnIdle()
  {
    _idleCallManager.AppOnIdle(() =>
    {
      _events.ModelSave -= TeklaEventsOnIdle;
      _events.UnRegister();
    });
  }
}
