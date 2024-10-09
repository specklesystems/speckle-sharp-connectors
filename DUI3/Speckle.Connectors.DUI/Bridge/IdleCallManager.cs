using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Speckle.Connectors.DUI.Bridge;

public interface IIdleCallManager
{
  void SubscribeToIdle(string id, Action action, Action addEvent);
  void SubscribeToIdle(string id, Func<Task> asyncAction, Action addEvent);
  void AppOnIdle(Action removeEvent);
}

//should be registered as singleton
[SuppressMessage("ReSharper", "InconsistentlySynchronizedField")]
public sealed class IdleCallManager : IIdleCallManager
{
  internal ConcurrentDictionary<string, Func<Task>> Calls { get; } = new();

  private readonly object _lock = new();
  public bool IdleSubscriptionCalled { get; private set; }

  private readonly ITopLevelExceptionHandler _topLevelExceptionHandler;

  public IdleCallManager(ITopLevelExceptionHandler topLevelExceptionHandler)
  {
    _topLevelExceptionHandler = topLevelExceptionHandler;
  }

  public void SubscribeToIdle(string id, Action action, Action addEvent) =>
    SubscribeToIdle(
      id,
      () =>
      {
        action.Invoke();
        return Task.CompletedTask;
      },
      addEvent
    );

  public void SubscribeToIdle(string id, Func<Task> asyncAction, Action addEvent) =>
    _topLevelExceptionHandler.CatchUnhandled(() => SubscribeInternal(id, asyncAction, addEvent));

  internal void SubscribeInternal(string id, Func<Task> action, Action addEvent)
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
    _topLevelExceptionHandler.FireAndForget(async () => await AppOnIdleInternal(removeEvent).ConfigureAwait(false));

  internal async Task AppOnIdleInternal(Action removeEvent)
  {
    foreach (KeyValuePair<string, Func<Task>> kvp in Calls)
    {
      await _topLevelExceptionHandler.CatchUnhandledAsync(kvp.Value).ConfigureAwait(false);
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
