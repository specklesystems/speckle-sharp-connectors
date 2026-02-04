using System.Collections.Concurrent;
using Speckle.InterfaceGenerator;

namespace Speckle.Connectors.DUI.Bridge;

/// <remarks>
/// This class was initially designed as an evolution
/// of hostapp specific idle managers, since they followed a similar logic.
/// However, has ended up a little over-engineered, so since then, for Revit connector
/// we've started to prefer a simpler solution that fits only the needs of said host app.
/// </remarks>
//should be registered as singleton
[GenerateAutoInterface]
public sealed class IdleCallManager(ITopLevelExceptionHandler topLevelExceptionHandler) : IIdleCallManager
{
  internal ConcurrentDictionary<string, Func<Task>> Calls { get; } = new();
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
    if (!Calls.TryAdd(id, action))
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
    foreach (KeyValuePair<string, Func<Task>> kvp in Calls)
    {
      await topLevelExceptionHandler.CatchUnhandledAsync(kvp.Value);
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
