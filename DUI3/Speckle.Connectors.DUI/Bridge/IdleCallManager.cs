using System.Collections.Concurrent;
using Speckle.InterfaceGenerator;

namespace Speckle.Connectors.DUI.Bridge;

[GenerateAutoInterface]
public class IdleCallManager : IIdleCallManager
{
  private readonly ITopLevelExceptionHandler _topLevelExceptionHandler;
  private readonly ConcurrentDictionary<string, Action> _calls = new();

  // POC: still not thread safe
  private volatile bool _hasSubscribed;

  public IdleCallManager(ITopLevelExceptionHandler topLevelExceptionHandler)
  {
    _topLevelExceptionHandler = topLevelExceptionHandler;
  }

  public bool TrySubscribeToIdle(Action action)
  {
    // POC: key for method is brittle | thread safe is not this is
    // I want to be called back ONCE when the host app has become idle once more
    // would this work "action.Method.Name" with anonymous function, including the SAME function
    // does this work across class instances? Should it? What about functions of the same name? Fully qualified name might be better
    _calls[action.Method.Name] = action;

    if (_hasSubscribed)
    {
      return false;
    }

    _hasSubscribed = true;
    return true;
  }

  public void AppOnIdle(Action onIdle)
  {
    _topLevelExceptionHandler.CatchUnhandled(() =>
    {
      foreach (KeyValuePair<string, Action> kvp in _calls)
      {
        kvp.Value.Invoke();
      }

      _calls.Clear();
      onIdle.Invoke();

      // setting last will delay entering re-subscription
      _hasSubscribed = false;
    });
  }
}
