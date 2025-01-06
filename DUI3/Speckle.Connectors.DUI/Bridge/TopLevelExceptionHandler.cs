using Microsoft.Extensions.Logging;
using Speckle.Connectors.DUI.Bindings;
using Speckle.InterfaceGenerator;
using Speckle.Sdk;
using Speckle.Sdk.Models.Extensions;

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
  public IBrowserBridge Parent { get; }
  public string Name => nameof(TopLevelExceptionHandler);

  private const string UNHANDLED_LOGGER_TEMPLATE = "An unhandled Exception occured";

  internal TopLevelExceptionHandler(ILogger<TopLevelExceptionHandler> logger, IBrowserBridge bridge)
  {
    _logger = logger;
    Parent = bridge;
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
        await function();
        return new Result();
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogError(ex, UNHANDLED_LOGGER_TEMPLATE);
        await SetGlobalNotification(
          ToastNotificationType.DANGER,
          "Unhandled Exception Occured",
          ex.ToFormattedString(),
          false
        );
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
        return new(await function.Invoke());
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        await HandleException(ex);
        return new(ex);
      }
    }
    catch (Exception ex)
    {
      _logger.LogCritical(ex, UNHANDLED_LOGGER_TEMPLATE);
      throw;
    }
  }

  private async Task HandleException(Exception ex)
  {
    _logger.LogError(ex, UNHANDLED_LOGGER_TEMPLATE);

    try
    {
      await SetGlobalNotification(
        ToastNotificationType.DANGER,
        "Unhandled Exception Occured",
        ex.ToFormattedString(),
        false
      );
    }
    catch (Exception toastEx)
    {
      // Not only was a top level exception caught, but our attempt to display a toast failed!
      // Toasts can fail if the BrowserBridge is not yet associated with a binding
      // For this reason, binding authors should avoid doing anything in
      // the constructors of bindings that may try and use the bridge!
      AggregateException aggregateException =
        new("An Unhandled top level exception was caught, and the toast failed to display it!", [toastEx, ex]);

      throw aggregateException;
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
  public async void FireAndForget(Func<Task> function) => await CatchUnhandledAsync(function);

  private async Task SetGlobalNotification(ToastNotificationType type, string title, string message, bool autoClose) =>
    await Parent.Send(
      BasicConnectorBindingCommands.SET_GLOBAL_NOTIFICATION, //TODO: We could move these constants into a DUI3 constants static class
      new
      {
        type,
        title,
        description = message,
        autoClose
      }
    );
}
