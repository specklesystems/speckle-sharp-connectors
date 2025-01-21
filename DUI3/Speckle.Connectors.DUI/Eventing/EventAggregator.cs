using Microsoft.Extensions.DependencyInjection;

namespace Speckle.Connectors.DUI.Eventing;

public interface IEventAggregator
{
  TEventType GetEvent<TEventType>()
    where TEventType : EventBase;
}

//based on Prism.Events at verison 8
// which was MIT https://github.com/PrismLibrary/Prism/tree/952e343f585b068ccb7d3478d3982485253a0508/src/Prism.Events
// License https://github.com/PrismLibrary/Prism/blob/952e343f585b068ccb7d3478d3982485253a0508/LICENSE
public class EventAggregator(IServiceProvider serviceProvider) : IEventAggregator
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
