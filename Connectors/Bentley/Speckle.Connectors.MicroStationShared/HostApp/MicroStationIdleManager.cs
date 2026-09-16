using System.Windows.Threading;
using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.MicroStation.HostApp;

/// <summary>
/// Schedules deferred operations during MicroStation application idle time.
/// Uses a WPF <see cref="DispatcherTimer"/> at <see cref="DispatcherPriority.ApplicationIdle"/>
/// as a substitute for a dedicated idle event (the MicroStation 2026 COM timer fires at a
/// fixed 1/60-second resolution which is coarser than needed for UI responsiveness).
/// </summary>
public sealed class MicroStationIdleManager : AppIdleManager
{
  private readonly IIdleCallManager _idleCallManager;

  public MicroStationIdleManager(IIdleCallManager idleCallManager)
    : base(idleCallManager)
  {
    _idleCallManager = idleCallManager;
  }

  protected override void AddEvent()
  {
    var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromMilliseconds(100) };
    timer.Tick += OnTick;
    timer.Start();
  }

  private void OnTick(object? sender, EventArgs e)
  {
    var timer = (DispatcherTimer)sender!;
    timer.Stop();
    timer.Tick -= OnTick;
    _idleCallManager.AppOnIdle(() => { });
  }
}
