using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Speckle.Connectors.DUI.Bindings;

namespace Speckle.Connectors.DUI.Bridge;

public interface IIdleCallManager
{
  void SubscribeToIdle(string id, Action action, Action addEvent);
  void AppOnIdle(Action removeEvent);
}

//should be registered as singleton
[SuppressMessage("ReSharper", "InconsistentlySynchronizedField")]
public class IdleCallManager : IIdleCallManager
{
  public ConcurrentDictionary<string, Action> Calls { get; } = new();

  private readonly object _lock = new();
  public bool IdleSubscriptionCalled { get; private set; }

  private readonly ITopLevelExceptionHandler _topLevelExceptionHandler;

  public IdleCallManager(TopLevelExceptionHandlerBinding binding)
    : this(binding.Parent.TopLevelExceptionHandler) { }

  internal IdleCallManager(ITopLevelExceptionHandler topLevelExceptionHandler)
  {
    _topLevelExceptionHandler = topLevelExceptionHandler;
  }

  public void SubscribeToIdle(string id, Action action, Action addEvent) =>
    _topLevelExceptionHandler.CatchUnhandled(() => SubscribeInternal(id, action, addEvent));

  public void SubscribeInternal(string id, Action action, Action addEvent)
  {
    Calls.TryAdd(id, action);
    if (!IdleSubscriptionCalled)
    {
      lock (_lock)
      {
        if (!IdleSubscriptionCalled)
        {
          addEvent.Invoke();
          IdleSubscriptionCalled = true;
        }
      }
    }
  }

  public void AppOnIdle(Action removeEvent) =>
    _topLevelExceptionHandler.CatchUnhandled(() => AppOnIdleInternal(removeEvent));

  public void AppOnIdleInternal(Action removeEvent)
  {
    foreach (KeyValuePair<string, Action> kvp in Calls)
    {
      kvp.Value.Invoke();
    }
    Calls.Clear();
    if (IdleSubscriptionCalled)
    {
      lock (_lock)
      {
        if (IdleSubscriptionCalled)
        {
          removeEvent.Invoke();
          IdleSubscriptionCalled = false;
        }
      }
    }
  }
}
