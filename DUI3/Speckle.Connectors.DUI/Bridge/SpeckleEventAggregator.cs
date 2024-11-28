using Microsoft.Extensions.DependencyInjection;

namespace Speckle.Connectors.DUI.Bridge;

public interface ISpeckleEventAggregator
{
  TEventType GetEvent<TEventType>()
    where TEventType : EventBase;
}

public class SpeckleEventAggregator : ISpeckleEventAggregator
{
  private readonly IServiceProvider _serviceProvider;

  private readonly Dictionary<Type, EventBase> _events = new();

  public SpeckleEventAggregator(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public TEventType GetEvent<TEventType>()
    where TEventType : EventBase
  {
    lock (_events)
    {
      if (!_events.TryGetValue(typeof(TEventType), out var existingEvent))
      {
        existingEvent = (TEventType)_serviceProvider.GetRequiredService(typeof(TEventType));
        _events[typeof(TEventType)] = existingEvent;
      }
      return (TEventType)existingEvent;
    }
  }
}
