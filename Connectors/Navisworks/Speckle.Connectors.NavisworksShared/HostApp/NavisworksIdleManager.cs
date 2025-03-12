using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connector.Navisworks.HostApp;

/// <summary>
/// Manages the scheduling of deferred operations during Navisworks idle periods.
/// Ensures UI updates and operations are batched efficiently to prevent UI freezing.
/// </summary>
public sealed class NavisworksIdleManager : AppIdleManager
{
  private readonly IIdleCallManager _idleCallManager;

  /// <summary>
  /// Initializes a new instance of the NavisworksIdleManager.
  /// </summary>
  /// <param name="idleCallManager">The manager responsible for queuing and executing deferred operations.</param>
  public NavisworksIdleManager(IIdleCallManager idleCallManager)
    : base(idleCallManager)
  {
    _idleCallManager = idleCallManager;
  }

  /// <summary>
  /// Subscribes to Navisworks idle events when operations are queued.
  /// </summary>
  protected override void AddEvent() => NavisworksApp.Idle += NavisworksAppOnIdle;

  private void NavisworksAppOnIdle(object? sender, EventArgs e) =>
    _idleCallManager.AppOnIdle(() => NavisworksApp.Idle -= NavisworksAppOnIdle);
}
