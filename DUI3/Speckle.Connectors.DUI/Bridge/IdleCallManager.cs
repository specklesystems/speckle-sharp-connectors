using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Speckle.InterfaceGenerator;

namespace Speckle.Connectors.DUI.Bridge;

//should be registered as singleton
[GenerateAutoInterface]
[SuppressMessage("ReSharper", "InconsistentlySynchronizedField")]
public class IdleCallManager(ITopLevelExceptionHandler topLevelExceptionHandler) : IIdleCallManager
{
  private readonly ConcurrentDictionary<string, Action> _calls = new();
  private bool _idleSubscriptionCalled;

  public void SubscribeToIdle(string id, Action action, Action addEvent) =>
    topLevelExceptionHandler.CatchUnhandled(() =>
    {
      _calls.TryAdd(id, action);
      if (!_idleSubscriptionCalled)
      {
        lock (_calls)
        {
          if (!_idleSubscriptionCalled)
          {
            addEvent.Invoke();
            _idleSubscriptionCalled = true;
          }
        }
      }
    });

  public void AppOnIdle(Action removeEvent) =>
    topLevelExceptionHandler.CatchUnhandled(() =>
    {
      foreach (KeyValuePair<string, Action> kvp in _calls)
      {
        kvp.Value.Invoke();
      }
      _calls.Clear();
      if (_idleSubscriptionCalled)
      {
        lock (_calls)
        {
          if (_idleSubscriptionCalled)
          {
            removeEvent.Invoke();
            _idleSubscriptionCalled = false;
          }
        }
      }
    });
}
