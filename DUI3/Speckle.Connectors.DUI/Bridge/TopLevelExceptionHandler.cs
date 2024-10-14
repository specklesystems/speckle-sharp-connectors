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

  /// <summary>
  /// When <see langword="true"/>, this <see cref="ITopLevelExceptionHandler"/> will not throw exceptions if the <see cref="Parent"/> <see cref="IBrowserBridge"/> is not initialized.
  /// </summary>
  /// <remarks>
  /// Most uses of a <see cref="ITopLevelExceptionHandler"/> are from <see cref="IBinding"/> where we should allways expect an functional <see cref="IBrowserBridge"/> (<see cref="IPostInitBinding"/>)
  /// However, some usages of a <see cref="ITopLevelExceptionHandler"/> outside of a <see cref="IBinding"/> (e.g. injected into non <see cref="IBinding"/>s via the <see cref="TopLevelExceptionHandlerBinding"/>)
  /// may want to use the logging capabilities of the <see cref="TopLevelExceptionHandler"/> before the <see cref="IBrowserBridge"/> is fully operational.
  /// TL;DR: Bindings should use <see cref="IPostInitBinding"/> and <see cref="AllowUseWithoutBrowser"/> <see langword="false"/>
  /// Any other usages can decide for them selves
  /// </remarks>
  public bool AllowUseWithoutBrowser { get; set; }

  private const string UNHANDLED_LOGGER_TEMPLATE = "An unhandled Exception occured from {binding}";

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
  public void CatchUnhandled(Action function) =>
    CatchUnhandledAsync(() =>
      {
        function();
        return Task.CompletedTask;
      })
      .GetAwaiter()
      .GetResult();

  /// <inheritdoc cref="CatchUnhandled(Action)"/>
  /// <typeparam name="T"><paramref name="function"/> return type</typeparam>
  /// <returns>A result pattern struct (where exceptions have been handled)</returns>
  public Result<T> CatchUnhandled<T>(Func<T> function) =>
    CatchUnhandledAsync(() => Task.FromResult(function.Invoke())).GetAwaiter().GetResult();

  /// <inheritdoc cref="CatchUnhandled(Action)"/>
  /// <returns>A result pattern struct (where exceptions have been handled)</returns>
  public async Task CatchUnhandledAsync(Func<Task> function) =>
    _ = await CatchUnhandledAsync<object?>(async () =>
      {
        await function().ConfigureAwait(false);
        return null;
      })
      .ConfigureAwait(false);

  ///<inheritdoc cref="CatchUnhandled{T}(Func{T})"/>
  public async Task<Result<T>> CatchUnhandledAsync<T>(Func<Task<T>> function)
  {
    try
    {
      if (!AllowUseWithoutBrowser)
      {
        Parent.AssertBindingInitialised();
      }

      try
      {
        return new(await function.Invoke().ConfigureAwait(false));
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogError(ex, UNHANDLED_LOGGER_TEMPLATE, BindingName);
        await SetGlobalNotification(
            ToastNotificationType.DANGER,
            "Unhandled Exception Occured",
            ex.ToFormattedString(),
            false
          )
          .ConfigureAwait(false);
        return new(ex);
      }
    }
    catch (Exception ex)
    {
      _logger.LogCritical(ex, UNHANDLED_LOGGER_TEMPLATE, BindingName);
      throw;
    }
  }

  private string? BindingName => Parent.IsBindingInitialized ? Parent.FrontendBoundName : null;

  /// <summary>
  /// Triggers an async action without explicitly needing to await it. <br/>
  /// Any <see cref="Exception"/> thrown by invoking <paramref name="function"/> will be handled by the <see cref="ITopLevelExceptionHandler"/><br/>
  /// </summary>
  /// <remarks>
  /// This <see langword="async"/> <see langword="void"/> function should only be used as an event handler that doesn't allow for handlers to return a <see cref="Task"/>
  /// In cases where you can use <see langword="await"/> keyword, you should prefer using <see cref="CatchUnhandledAsync"/>
  /// </remarks>
  public async void FireAndForget(Func<Task> function) => await CatchUnhandledAsync(function).ConfigureAwait(false);

  private async Task SetGlobalNotification(ToastNotificationType type, string title, string message, bool autoClose) =>
    await Parent
      .Send(
        BasicConnectorBindingCommands.SET_GLOBAL_NOTIFICATION, //TODO: We could move these constants into a DUI3 constants static class
        new
        {
          type,
          title,
          description = message,
          autoClose
        }
      )
      .ConfigureAwait(false);
}
