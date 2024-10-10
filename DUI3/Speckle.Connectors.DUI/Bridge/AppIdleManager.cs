using Speckle.InterfaceGenerator;

namespace Speckle.Connectors.DUI.Bridge;

[GenerateAutoInterface]
public abstract class AppIdleManager : IAppIdleManager
{
  private readonly IIdleCallManager _idleCallManager;

  protected AppIdleManager(IIdleCallManager idleCallManager)
  {
    _idleCallManager = idleCallManager;
  }

  /// <summary>
  /// Subscribe deferred action to Idling event to run it whenever Revit becomes idle.
  /// </summary>
  /// <param name="action"> Action to call whenever the host app becomes Idle.</param>
  /// some events in host app are triggered many times, we might get 10x per object
  /// Making this more like a deferred action, so we don't update the UI many times
  public void SubscribeToIdle(string id, Action action)
  {
    _idleCallManager.SubscribeToIdle(id, action, AddEvent);
  }

  /// <inheritdoc cref="SubscribeToIdle(string,System.Action)"/>
  public void SubscribeToIdle(string id, Func<Task> asyncAction)
  {
    _idleCallManager.SubscribeToIdle(id, asyncAction, AddEvent);
  }

  protected abstract void AddEvent();
}
