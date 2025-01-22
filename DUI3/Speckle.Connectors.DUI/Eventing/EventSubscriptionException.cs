using Speckle.Sdk;

namespace Speckle.Connectors.DUI.Eventing;

public class EventSubscriptionException : SpeckleException
{
  public EventSubscriptionException(string message)
    : base(message) { }

  public EventSubscriptionException() { }

  public EventSubscriptionException(string message, Exception innerException)
    : base(message, innerException) { }
}
