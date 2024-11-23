using Autodesk.Navisworks.Api;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connector.Navisworks.Bindings;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;

namespace Speckle.Connector.Navisworks.HostApp;

/// <summary>
/// Manages document state change notifications for the Navisworks connector.
/// Coalesces various document events into batched UI updates using the idle manager.
/// </summary>
public sealed class NavisworksDocumentEvents : IDisposable
{
  private readonly IServiceProvider _serviceProvider;
  private readonly ITopLevelExceptionHandler _topLevelExceptionHandler;
  private readonly IAppIdleManager _idleManager;
  private bool _isSubscribed;
  private readonly object _subscriptionLock = new();
  private bool _disposed;

  /// <summary>
  /// Initializes event handling for document and model changes.
  /// </summary>
  public NavisworksDocumentEvents(
    IServiceProvider serviceProvider,
    ITopLevelExceptionHandler topLevelExceptionHandler,
    IAppIdleManager idleManager
  )
  {
    _serviceProvider = serviceProvider;
    _topLevelExceptionHandler = topLevelExceptionHandler;
    _idleManager = idleManager;
    SubscribeToEvents();
  }

  // public void Initialize() => SubscribeToEvents();

  /// <summary>
  /// Subscribes to document-level events and model collection changes.
  /// </summary>
  private void SubscribeToEvents()
  {
    lock (_subscriptionLock)
    {
      if (_isSubscribed)
      {
        return;
      }

      if (NavisworksApp.ActiveDocument != null)
      {
        SubscribeToModelEvents(NavisworksApp.ActiveDocument);
      }

      _isSubscribed = true;
    }
  }

  private void SubscribeToModelEvents(Document document) => document.Models.CollectionChanged += OnDocumentEvent;

  /// <summary>
  /// Queues a document change notification to be processed during idle time.
  /// </summary>
  private void OnDocumentEvent(object sender, EventArgs e) =>
    _topLevelExceptionHandler.CatchUnhandled(
      () =>
        _idleManager.SubscribeToIdle(
          nameof(NavisworksDocumentEvents),
          async () => await NotifyDocumentChanged().ConfigureAwait(false)
        )
    );

  private async Task NotifyDocumentChanged()
  {
    var store = _serviceProvider.GetRequiredService<DocumentModelStore>();
    var basicBinding = _serviceProvider.GetRequiredService<IBasicConnectorBinding>();
    var commands = (basicBinding as NavisworksBasicConnectorBinding)?.Commands;

    // Check if we have a blank document state (no models)
    if (NavisworksApp.ActiveDocument.Models.Count == 0)
    {
      // Clear the store when there are no models
      store.Models.Clear();
    }

    if (commands != null)
    {
      await commands.NotifyDocumentChanged().ConfigureAwait(false);
    }
  }

  private void UnsubscribeFromEvents()
  {
    if (NavisworksApp.ActiveDocument != null)
    {
      UnsubscribeFromModelEvents(NavisworksApp.ActiveDocument);
    }
  }

  private void UnsubscribeFromModelEvents(Document document)
  {
    document.Models.CollectionChanged -= OnDocumentEvent;

    var sendBinding = _serviceProvider.GetRequiredService<NavisworksSendBinding>();
    sendBinding.CancelAllSendOperations();
  }

  public void Dispose()
  {
    Dispose(true);
    GC.SuppressFinalize(this);
  }

  private void Dispose(bool disposing)
  {
    if (_disposed)
    {
      return;
    }

    if (disposing)
    {
      UnsubscribeFromEvents();
      _isSubscribed = false;
    }

    _disposed = true;
  }

  ~NavisworksDocumentEvents()
  {
    Dispose(false);
  }
}
