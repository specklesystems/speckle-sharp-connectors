namespace Speckle.Connectors.DUI.Eventing;

//based on Prism.Events
public sealed class SubscriptionToken(Action<SubscriptionToken> unsubscribeAction)
  : IEquatable<SubscriptionToken>,
    IDisposable
{
  private readonly Guid _token = Guid.NewGuid();
  private Action<SubscriptionToken>? _unsubscribeAction = unsubscribeAction;

  public bool Equals(SubscriptionToken? other)
  {
    if (other == null)
    {
      return false;
    }

    return Equals(_token, other._token);
  }

  public override bool Equals(object? obj)
  {
    if (ReferenceEquals(this, obj))
    {
      return true;
    }

    return Equals(obj as SubscriptionToken);
  }

  public override int GetHashCode() => _token.GetHashCode();

  public bool IsActive => _unsubscribeAction != null;

  public void Unsubscribe() => _unsubscribeAction = null;

  public void Dispose()
  {
    // While the SubscriptionToken class implements IDisposable, in the case of weak subscriptions
    // (i.e. keepSubscriberReferenceAlive set to false in the Subscribe method) it's not necessary to unsubscribe,
    // as no resources should be kept alive by the event subscription.
    // In such cases, if a warning is issued, it could be suppressed.

    if (_unsubscribeAction != null)
    {
      _unsubscribeAction(this);
      Unsubscribe();
    }
  }
}
