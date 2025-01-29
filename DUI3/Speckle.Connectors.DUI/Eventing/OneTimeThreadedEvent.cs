using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.DUI.Eventing;

public abstract class OneTimeThreadedEvent<T>(IThreadContext threadContext, ITopLevelExceptionHandler exceptionHandler)
  : SpeckleEvent<T>(threadContext, exceptionHandler)
  where T : notnull
{
  private readonly Dictionary<string, SubscriptionToken> _activeTokens = new();

  public SubscriptionToken OneTimeSubscribe(
    string id,
    Func<T, Task> action,
    ThreadOption threadOption = ThreadOption.PublisherThread
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
      token = Subscribe(action, threadOption, EventFeatures.OneTime | EventFeatures.IsAsync);
      _activeTokens.Add(id, token);
      return token;
    }
  }

  public SubscriptionToken OneTimeSubscribe(
    string id,
    Action<T> action,
    ThreadOption threadOption = ThreadOption.PublisherThread
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
      token = Subscribe(action, threadOption, EventFeatures.OneTime);
      _activeTokens.Add(id, token);
      return token;
    }
  }

  public async Task PublishAsync(T payload)
  {
    SubscriptionToken[] tokensToDestory = [];
    lock (_activeTokens)
    {
      if (_activeTokens.Count > 0)
      {
        tokensToDestory = _activeTokens.Values.ToArray();
        _activeTokens.Clear();
      }
    }
    await InternalPublish(payload);
    if (tokensToDestory.Length > 0)
    {
      foreach (var token in tokensToDestory)
      {
        token.Dispose();
      }
    }
  }
}
