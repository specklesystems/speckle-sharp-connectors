using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.DUI.Eventing;

public abstract class OneTimeThreadedEvent<T>(IThreadContext threadContext, ITopLevelExceptionHandler exceptionHandler)
  : ThreadedEvent<T>(threadContext, exceptionHandler)
  where T : notnull
{
  private readonly Dictionary<string, SubscriptionToken> _activeTokens = new();

  public SubscriptionToken OneTimeSubscribe(
    string id,
    Func<T, Task> action,
    ThreadOption threadOption = ThreadOption.PublisherThread,
    bool keepSubscriberReferenceAlive = false,
    Predicate<T>? filter = null
  )
  {
    return OneTimeInternal(id, t => action(t), threadOption, keepSubscriberReferenceAlive, filter);
  }

  public SubscriptionToken OneTimeSubscribe(
    string id,
    Func<Task> action,
    ThreadOption threadOption = ThreadOption.PublisherThread,
    bool keepSubscriberReferenceAlive = false,
    Predicate<T>? filter = null
  )
  {
    return OneTimeInternal(id, _ => action(), threadOption, keepSubscriberReferenceAlive, filter);
  }

  public SubscriptionToken OneTimeSubscribe(
    string id,
    Action<T> action,
    ThreadOption threadOption = ThreadOption.PublisherThread,
    bool keepSubscriberReferenceAlive = false,
    Predicate<T>? filter = null
  )
  {
    return OneTimeInternal(id, action, threadOption, keepSubscriberReferenceAlive, filter);
  }

  public SubscriptionToken OneTimeSubscribe(
    string id,
    Action action,
    ThreadOption threadOption = ThreadOption.PublisherThread,
    bool keepSubscriberReferenceAlive = false,
    Predicate<T>? filter = null
  )
  {
    return OneTimeInternal(id, _ => action(), threadOption, keepSubscriberReferenceAlive, filter);
  }

  private SubscriptionToken OneTimeInternal(
    string id,
    Action<T> action,
    ThreadOption threadOption,
    bool keepSubscriberReferenceAlive,
    Predicate<T>? filter
  )
  {
    lock (_activeTokens)
    {
      if (_activeTokens.TryGetValue(id, out var token))
      {
        if (token.IsActive)
        {
          return token;
        }
        _activeTokens.Remove(id);
      }
      token = SubscribeOnceOrNot(action, threadOption, keepSubscriberReferenceAlive, filter, true);
      _activeTokens.Add(id, token);
      return token;
    }
  }

  public override void Publish(T payload)
  {
    lock (_activeTokens)
    {
      base.Publish(payload);
      foreach (var token in _activeTokens.Values)
      {
        token.Dispose();
      }
      _activeTokens.Clear();
    }
  }
}
