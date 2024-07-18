using System.Collections.Concurrent;
using Speckle.InterfaceGenerator;

namespace Speckle.Connectors.DUI.Bridge;

//should be registered as singleton
[GenerateAutoInterface]
public class IdleCallManager(ITopLevelExceptionHandler topLevelExceptionHandler) : IIdleCallManager
{
  private readonly ConcurrentDictionary<string, Action> _calls = new();

  public bool TrySubscribeToIdle(string id, Action action) =>
    // POC: key for method is brittle | thread safe is not this is
    // I want to be called back ONCE when the host app has become idle once more
    // would this work "action.Method.Name" with anonymous function, including the SAME function
    // does this work across class instances? Should it? What about functions of the same name? Fully qualified name might be better
    _calls.TryAdd(id, action);

  public void AppOnIdle(Action onIdle) =>
    topLevelExceptionHandler.CatchUnhandled(() =>
    {
      foreach (KeyValuePair<string, Action> kvp in _calls)
      {
        kvp.Value.Invoke();
      }

      _calls.Clear();
      onIdle.Invoke();
    });
}
