using Speckle.Connectors.DUI.Bridge;
using Tekla.Structures.Model;

namespace Speckle.Connectors.TeklaShared.HostApp;

public sealed class TeklaIdleManager : AppIdleManager
{
  private readonly IIdleCallManager _idleCallManager;
  private readonly Events _events;

  public TeklaIdleManager(IIdleCallManager idleCallManager, Events events)
    : base(idleCallManager)
  {
    _idleCallManager = idleCallManager;
    _events = events;
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
