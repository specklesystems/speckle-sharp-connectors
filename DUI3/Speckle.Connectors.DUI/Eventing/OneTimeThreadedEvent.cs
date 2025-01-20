using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.DUI.Eventing;

public abstract class OneTimeThreadedEvent<T>(IThreadContext threadContext, ITopLevelExceptionHandler exceptionHandler)
  : ThreadedEvent<T>(threadContext, exceptionHandler), IDisposable
  where T : notnull
{
  private readonly SemaphoreSlim _semaphore = new(1, 1);
  private readonly Dictionary<string, SubscriptionToken> _activeTokens = new();

  protected virtual void Dispose(bool isDisposing)
  {
    if (isDisposing)
    {
      _semaphore.Dispose();
    }
  }
  
  public void Dispose()
  {
    Dispose(true);
    GC.SuppressFinalize(this);
  }
  
  ~OneTimeThreadedEvent() => Dispose(false);

  public SubscriptionToken OneTimeSubscribe(
    string id,
    Func<T, Task> action,
    ThreadOption threadOption = ThreadOption.PublisherThread,
    bool keepSubscriberReferenceAlive = false,
    Predicate<T>? filter = null
  ) =>
    OneTimeInternal(id, action, threadOption, keepSubscriberReferenceAlive, filter);

  public SubscriptionToken OneTimeSubscribe(
    string id,
    Func<Task> action,
    ThreadOption threadOption = ThreadOption.PublisherThread,
    bool keepSubscriberReferenceAlive = false,
    Predicate<T>? filter = null
  ) =>
    OneTimeInternal(id, _ => action(), threadOption, keepSubscriberReferenceAlive, filter);

  public SubscriptionToken OneTimeSubscribe(
    string id,
    Action action,
    ThreadOption threadOption = ThreadOption.PublisherThread,
    bool keepSubscriberReferenceAlive = false,
    Predicate<T>? filter = null
  ) =>
    OneTimeInternal(id, _ =>
    {
      action();
      return Task.CompletedTask;
    }, threadOption, keepSubscriberReferenceAlive, filter);
  public SubscriptionToken OneTimeSubscribe(
    string id,
    Action<T> action,
    ThreadOption threadOption = ThreadOption.PublisherThread,
    bool keepSubscriberReferenceAlive = false,
    Predicate<T>? filter = null
  ) =>
    OneTimeInternal(id, t =>
    {
      action(t);
      return Task.CompletedTask;
    }, threadOption, keepSubscriberReferenceAlive, filter);
  
  private SubscriptionToken OneTimeInternal(
    string id,
    Func<T, Task> action,
    ThreadOption threadOption,
    bool keepSubscriberReferenceAlive,
    Predicate<T>? filter
  )
  {
     _semaphore.Wait();
    try
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
    } finally
    {
      _semaphore.Release();
    }
  }

  public override async Task PublishAsync(T payload)
  {
    await _semaphore.WaitAsync();
    try
    {
      await base.PublishAsync(payload);
      foreach (var token in _activeTokens.Values)
      {
        token.Dispose();
      }

      _activeTokens.Clear();
    }
    finally
    {
      _semaphore.Release();
    }
  }
}
