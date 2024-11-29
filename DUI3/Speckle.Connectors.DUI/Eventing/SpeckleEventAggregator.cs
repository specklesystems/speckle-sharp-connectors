using Microsoft.Extensions.DependencyInjection;

namespace Speckle.Connectors.DUI.Eventing;

public interface ISpeckleEventAggregator
{
  TEventType GetEvent<TEventType>()
    where TEventType : EventBase;
}

//based on Prism.Events
public class SpeckleEventAggregator(IServiceProvider serviceProvider) : ISpeckleEventAggregator
{
  private readonly Dictionary<Type, EventBase> _events = new();

  public TEventType GetEvent<TEventType>()
    where TEventType : EventBase
  {
    lock (_events)
    {
      if (!_events.TryGetValue(typeof(TEventType), out var existingEvent))
      {
        existingEvent = (TEventType)serviceProvider.GetRequiredService(typeof(TEventType));
        _events[typeof(TEventType)] = existingEvent;
      }
      return (TEventType)existingEvent;
    }
  }
}
