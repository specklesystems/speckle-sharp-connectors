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
  private bool _isProcessing;
  private int _priorModelCount;
  private int _finalModelCount;

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

  private void SubscribeToModelEvents(Document document)
  {
    document.Models.CollectionChanging += OnDocumentModelCountChanging;
    document.Models.CollectionChanged += OnDocumentModelCountChanged;
  }

  private void OnDocumentModelCountChanging(object sender, EventArgs e) =>
    _priorModelCount = ((Document)sender).Models.Count;

  /// <summary>
  /// Queues a document change notification to be processed during idle time.
  /// </summary>
  private void OnDocumentModelCountChanged(object sender, EventArgs e)
  {
    _finalModelCount = ((Document)sender).Models.Count;

    _topLevelExceptionHandler.CatchUnhandled(
      () =>
        _idleManager.SubscribeToIdle(
          nameof(NavisworksDocumentEvents),
          async () => await NotifyDocumentChanged().ConfigureAwait(false)
        )
    );
  }

  private async Task NotifyDocumentChanged()
  {
    if (_isProcessing)
    {
      return;
    }

    _isProcessing = true;

    try
    {
      var store = _serviceProvider.GetRequiredService<DocumentModelStore>();
      var basicBinding = _serviceProvider.GetRequiredService<IBasicConnectorBinding>();
      var commands = (basicBinding as NavisworksBasicConnectorBinding)?.Commands;

      switch (_finalModelCount)
      {
        // Check if we have a blank document state (no models)
        case 0 when _priorModelCount > 0:
          // Clear the store when there are no models
          store.Models.Clear();
          break;
        case > 0 when _priorModelCount == 0:
          // Read the current state from the active document
          store.ReadFromFile();
          break;
      }

      if (commands != null)
      {
        await commands.NotifyDocumentChanged().ConfigureAwait(false);
      }
    }
    finally
    {
      _isProcessing = false; // Reset the flag
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
    document.Models.CollectionChanged -= OnDocumentModelCountChanged;
    document.Models.CollectionChanging -= OnDocumentModelCountChanging;

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
