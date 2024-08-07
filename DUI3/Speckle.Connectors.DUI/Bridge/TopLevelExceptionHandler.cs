using Microsoft.Extensions.Logging;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Sdk.Models.Extensions;
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
  public IBridge Parent { get; }
  public string Name => nameof(TopLevelExceptionHandler);

  private const string UNHANDLED_LOGGER_TEMPLATE = "An unhandled Exception occured";

  internal TopLevelExceptionHandler(ILogger<TopLevelExceptionHandler> logger, IBridge bridge)
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
  public Result CatchUnhandled(Action function)
  {
    try
    {
      try
      {
        function.Invoke();
        return new();
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogError(ex, UNHANDLED_LOGGER_TEMPLATE);

        SetGlobalNotification(
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

  /// <inheritdoc cref="CatchUnhandled(Action)"/>
  /// <typeparam name="T"><paramref name="function"/> return type</typeparam>
  /// <returns>A result pattern struct (where exceptions have been handled)</returns>
  public Result<T> CatchUnhandled<T>(Func<T> function) =>
    CatchUnhandled(() => Task.FromResult(function.Invoke())).Result;

  ///<inheritdoc cref="CatchUnhandled{T}(Func{T})"/>
  public async Task<Result<T>> CatchUnhandled<T>(Func<Task<T>> function)
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
        SetGlobalNotification(
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

  private void SetGlobalNotification(ToastNotificationType type, string title, string message, bool autoClose) =>
    Parent.Send(
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
