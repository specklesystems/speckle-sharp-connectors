using System.Collections.Concurrent;
using Speckle.InterfaceGenerator;

namespace Speckle.Connectors.DUI.Bridge;

//should be registered as singleton
[GenerateAutoInterface]
public sealed class IdleCallManager(ITopLevelExceptionHandler topLevelExceptionHandler) : IIdleCallManager
{
  private readonly ConcurrentDictionary<string, Func<Task>> _calls = new();
  private readonly object _lock = new();
  public bool IdleSubscriptionCalled { get; private set; }

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

  public void SubscribeToIdle(string id, Func<Task> action, Action addEvent)
  {
    if (!_calls.TryAdd(id, action))
    {
      return;
    }

    if (!IdleSubscriptionCalled)
    {
      lock (_lock)
      {
        if (!IdleSubscriptionCalled)
        {
          topLevelExceptionHandler.CatchUnhandled(addEvent.Invoke);
          IdleSubscriptionCalled = true;
        }
      }
    }
  }

  public void AppOnIdle(Action removeEvent) =>
    topLevelExceptionHandler.FireAndForget(async () => await AppOnIdleInternal(removeEvent));

  internal async Task AppOnIdleInternal(Action removeEvent)
  {
    foreach (KeyValuePair<string, Func<Task>> kvp in _calls.ToList())
    {
      await topLevelExceptionHandler.CatchUnhandledAsync(kvp.Value);
    }

    _calls.Clear();
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
