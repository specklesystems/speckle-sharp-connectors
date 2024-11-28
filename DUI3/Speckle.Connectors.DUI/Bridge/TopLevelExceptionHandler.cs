using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Threading;
using Speckle.InterfaceGenerator;
using Speckle.Sdk;

namespace Speckle.Connectors.DUI.Bridge;

/// <summary>
/// The functions provided by this class are designed to be used in all "top level" scenarios (e.g. Plugin, UI, and Event callbacks)
/// To provide "last ditch effort" handling of unexpected exceptions that have not been handled.
///  1. Log events to the injected <see cref="ILogger"/>
///  2. Display a toast notification with exception details
/// <br/>
/// </summary>
/// <remarks>
/// <see cref="ExceptionHelpers.IsFatal"/> exceptions cannot be recovered from.
/// They will be rethrown to allow the host app to run its handlers<br/>
/// Depending on the host app, this may trigger windows event logging, and recovery snapshots before ultimately terminating the process<br/>
/// Attempting to swallow them may lead to data corruption, deadlocking, or things worse than a managed host app crash.
/// </remarks>
[GenerateAutoInterface]
public sealed class TopLevelExceptionHandler : ITopLevelExceptionHandler
{
  private readonly ILogger<TopLevelExceptionHandler> _logger;
  private readonly ISpeckleEventAggregator _eventAggregator;
  public string Name => nameof(TopLevelExceptionHandler);

  private const string UNHANDLED_LOGGER_TEMPLATE = "An unhandled Exception occured";

  internal TopLevelExceptionHandler(ILogger<TopLevelExceptionHandler> logger, ISpeckleEventAggregator eventAggregator)
  {
    _logger = logger;
    _eventAggregator = eventAggregator;
  }

  /// <summary>
  /// Invokes the given <paramref name="function"/> within a <see langword="try"/>/<see langword="catch"/> block,
  /// and provides exception handling for unexpected exceptions that have not been handled.<br/>
  /// </summary>
  /// <param name="function">The function to invoke and provide error handling for</param>
  /// <exception cref="Exception"><see cref="ExceptionHelpers.IsFatal"/> will be rethrown, these should be allowed to bubble up to the host app</exception>
  /// <seealso cref="ExceptionHelpers.IsFatal"/>
  public void CatchUnhandled(Action function)
  {
    _ = CatchUnhandled<object?>(() =>
    {
      function();
      return null;
    });
  }

  /// <inheritdoc cref="CatchUnhandled(Action)"/>
  /// <typeparam name="T"><paramref name="function"/> return type</typeparam>
  /// <returns>A result pattern struct (where exceptions have been handled)</returns>
  public Result<T> CatchUnhandled<T>(Func<T> function) =>
    CatchUnhandledAsync(() => Task.FromResult(function.Invoke())).Result; //Safe to do a .Result because this as an already completed and non-async Task from the Task.FromResult

  /// <inheritdoc cref="CatchUnhandled(Action)"/>
  /// <returns>A result pattern struct (where exceptions have been handled)</returns>
  public async Task<Result> CatchUnhandledAsync(Func<Task> function)
  {
    try
    {
      try
      {
        await function().ConfigureAwait(false);
        return new Result();
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogError(ex, UNHANDLED_LOGGER_TEMPLATE);
        _eventAggregator.GetEvent<ExceptionEvent>().Publish(ex);
        return new(ex);
      }
    }
    catch (Exception ex)
    {
      _logger.LogCritical(ex, UNHANDLED_LOGGER_TEMPLATE);
      throw;
    }
  }

  ///<inheritdoc cref="CatchUnhandled{T}(Func{T})"/>
  public async Task<Result<T>> CatchUnhandledAsync<T>(Func<Task<T>> function)
  {
    try
    {
      try
      {
        return new(await function.Invoke().ConfigureAwait(false));
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogError(ex, UNHANDLED_LOGGER_TEMPLATE);
        _eventAggregator.GetEvent<ExceptionEvent>().Publish(ex);
        return new(ex);
      }
    }
    catch (Exception ex)
    {
      _logger.LogCritical(ex, UNHANDLED_LOGGER_TEMPLATE);
      throw;
    }
  }

  /// <summary>
  /// Triggers an async action without explicitly needing to await it. <br/>
  /// Any <see cref="Exception"/> thrown by invoking <paramref name="function"/> will be handled by the <see cref="ITopLevelExceptionHandler"/><br/>
  /// </summary>
  /// <remarks>
  /// This <see langword="async"/> <see langword="void"/> function should only be used as an event handler that doesn't allow for handlers to return a <see cref="Task"/>
  /// In cases where you can use <see langword="await"/> keyword, you should prefer using <see cref="CatchUnhandledAsync"/>
  /// </remarks>
  /// <param name="function"><inheritdoc cref="CatchUnhandled{T}(Func{T})"/></param>
  public async void FireAndForget(Func<Task> function) => await CatchUnhandledAsync(function).ConfigureAwait(false);
}

public interface ISpeckleEventAggregator
{
  TEventType GetEvent<TEventType>() where TEventType : EventBase;
}
public class SpeckleEventAggregator : ISpeckleEventAggregator
{
  private readonly IServiceProvider _serviceProvider;
  

  private readonly Dictionary<Type, EventBase> _events = new();

  public SpeckleEventAggregator(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public TEventType GetEvent<TEventType>() where TEventType : EventBase
  {
    lock (_events)
    {
      if (!_events.TryGetValue(typeof(TEventType), out var existingEvent))
      {
        existingEvent = (TEventType)_serviceProvider.GetService(typeof(TEventType));
        _events[typeof(TEventType)] = existingEvent;
      }
      return (TEventType)existingEvent;
    }
  }
}

public class ExceptionEvent(IThreadContext threadContext) : SpeckleEvent<Exception>(threadContext);

public class SpeckleEvent<T>(IThreadContext threadContext) : PubSubEvent<T>
{
  public override SubscriptionToken Subscribe(Action<T> action, ThreadOption threadOption, bool keepSubscriberReferenceAlive,
    Predicate<T> filter) 
  {
    IDelegateReference actionReference = new DelegateReference(action, keepSubscriberReferenceAlive);

    EventSubscription subscription;
    switch (threadOption)
    {
      case ThreadOption.PublisherThread:
        subscription = new EventSubscription(actionReference);
        break;
      case ThreadOption.BackgroundThread:
        subscription = new BackgroundEventSubscription(actionReference);
        break;
      case ThreadOption.UIThread:
        subscription = new ThreadContextEventSubscription(actionReference, threadContext);
        break;
      default:
        subscription = new EventSubscription(actionReference);
        break;
    }

    return InternalSubscribe(subscription);
    
  }
}

public class ThreadContextEventSubscription : EventSubscription
{
  private readonly IThreadContext _threadContext;
  public ThreadContextEventSubscription(IDelegateReference actionReference, IThreadContext threadContext) : base(actionReference)
  {
    _threadContext = threadContext;
  }

  public override void InvokeAction(Action action) => _threadContext.RunOnMain(action);
}
