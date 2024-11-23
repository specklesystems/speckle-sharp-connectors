using Autodesk.Navisworks.Api;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connector.Navisworks.Bindings;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;

namespace Speckle.Connector.Navisworks.HostApp;

/// <summary>
/// Manages document and model state change notifications for the Navisworks connector.
/// Coalesces various document events into batched updates to be processed during idle time.
/// </summary>
public sealed class NavisworksDocumentEvents : IDisposable
{
  private readonly IServiceProvider _serviceProvider;
  private readonly ITopLevelExceptionHandler _topLevelExceptionHandler;
  private readonly IAppIdleManager _idleManager;
  private readonly object _subscriptionLock = new();

  private bool _isSubscribed;
  private bool _isProcessing;
  private bool _disposed;

  private int _priorModelCount;
  private int _finalModelCount;

  /// <summary>
  /// Initializes a new instance of the <see cref="NavisworksDocumentEvents"/> class and subscribes to document events.
  /// </summary>
  /// <param name="serviceProvider">The service provider for dependency injection.</param>
  /// <param name="topLevelExceptionHandler">Handles exceptions during event processing.</param>
  /// <param name="idleManager">Manages idle processing.</param>
  public NavisworksDocumentEvents(
    IServiceProvider serviceProvider,
    ITopLevelExceptionHandler topLevelExceptionHandler,
    IAppIdleManager idleManager
  )
  {
    _serviceProvider = serviceProvider;
    _topLevelExceptionHandler = topLevelExceptionHandler;
    _idleManager = idleManager;

    SubscribeToDocumentModelEvents();
  }

  /// <summary>
  /// Subscribes to document-level events to monitor model state changes.
  /// </summary>
  private void SubscribeToDocumentModelEvents()
  {
    lock (_subscriptionLock)
    {
      if (_isSubscribed)
      {
        return;
      }

      var activeDocument = NavisworksApp.ActiveDocument;
      if (activeDocument != null)
      {
        activeDocument.Models.CollectionChanging += HandleDocumentModelCountChanging;
        activeDocument.Models.CollectionChanged += HandleDocumentModelCountChanged;
      }

      _isSubscribed = true;
    }
  }

  /// <summary>
  /// Tracks the current model count before changes occur.
  /// </summary>
  private void HandleDocumentModelCountChanging(object sender, EventArgs e)
  {
    _priorModelCount = ((Document)sender).Models.Count;
  }

  /// <summary>
  /// Schedules processing of model count changes during idle time.
  /// </summary>
  private void HandleDocumentModelCountChanged(object sender, EventArgs e)
  {
    _finalModelCount = ((Document)sender).Models.Count;

    _topLevelExceptionHandler.CatchUnhandled(
      () =>
        _idleManager.SubscribeToIdle(
          nameof(NavisworksDocumentEvents),
          async () => await NotifyValidModelStateChange().ConfigureAwait(false)
        )
    );
  }

  /// <summary>
  /// Processes model state changes by updating the store and notifying commands.
  /// </summary>
  private async Task NotifyValidModelStateChange()
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
        case 0 when _priorModelCount > 0:
          // Clear the store when models are removed
          store.Models.Clear();
          break;
        case > 0 when _priorModelCount == 0:
          // Load state when models are added
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
      _isProcessing = false;
    }
  }

  private void UnsubscribeFromDocumentModelEvents()
  {
    var activeDocument = NavisworksApp.ActiveDocument;
    if (activeDocument != null)
    {
      UnsubscribeFromModelEvents(activeDocument);
    }

    _isSubscribed = false;
  }

  private void UnsubscribeFromModelEvents(Document document)
  {
    document.Models.CollectionChanged -= HandleDocumentModelCountChanged;
    document.Models.CollectionChanging -= HandleDocumentModelCountChanging;

    var sendBinding = _serviceProvider.GetRequiredService<NavisworksSendBinding>();
    sendBinding.CancelAllSendOperations();
  }

  /// <summary>
  /// Disposes of resources and unsubscribes from events.
  /// </summary>
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
      UnsubscribeFromDocumentModelEvents();
    }

    _disposed = true;
  }

  ~NavisworksDocumentEvents()
  {
    Dispose(false);
  }
}
