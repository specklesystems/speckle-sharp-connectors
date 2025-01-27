using Microsoft.Extensions.DependencyInjection;
using Speckle.Connector.Navisworks.Bindings;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Eventing;

namespace Speckle.Connector.Navisworks.HostApp;

/// <summary>
/// Manages document and model state change notifications for the Navisworks connector.
/// Coalesces various document events into batched updates to be processed during idle time.
/// </summary>
public sealed class NavisworksDocumentEvents : IDisposable
{
  private readonly IServiceProvider _serviceProvider;
  private readonly IEventAggregator _eventAggregator;
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
  public NavisworksDocumentEvents(
    IServiceProvider serviceProvider, IEventAggregator eventAggregator)
  {
    _serviceProvider = serviceProvider;
    _eventAggregator = eventAggregator;

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
  private void HandleDocumentModelCountChanging(object sender, EventArgs e) =>
    _priorModelCount = ((NAV.Document)sender).Models.Count;

  /// <summary>
  /// Schedules processing of model count changes during idle time.
  /// </summary>
  private void HandleDocumentModelCountChanged(object sender, EventArgs e)
  {
    _finalModelCount = ((NAV.Document)sender).Models.Count;

    _eventAggregator.GetEvent<IdleEvent>()
      .OneTimeSubscribe(nameof(NavisworksDocumentEvents), ProcessModelStateChangeAsync);

  }

  private async Task ProcessModelStateChangeAsync(object _)
  {
    if (_isProcessing)
    {
      return;
    }

    _isProcessing = true;

    try
    {
      var store = _serviceProvider.GetRequiredService<NavisworksDocumentModelStore>();
      var basicBinding = _serviceProvider.GetRequiredService<IBasicConnectorBinding>();
      var commands = (basicBinding as NavisworksBasicConnectorBinding)?.Commands;

      switch (_finalModelCount)
      {
        case 0 when _priorModelCount > 0:
          store.ClearAndSave();
          break;
        case > 0 when _priorModelCount == 0:
          store.ReloadState();
          break;
      }

      if (commands != null)
      {
        await commands.NotifyDocumentChanged();
      }
    }
    finally
    {
      _isProcessing = false;
    }
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
      var store = _serviceProvider.GetRequiredService<NavisworksDocumentModelStore>();
      var basicBinding = _serviceProvider.GetRequiredService<IBasicConnectorBinding>();
      var commands = (basicBinding as NavisworksBasicConnectorBinding)?.Commands;

      switch (_finalModelCount)
      {
        case 0 when _priorModelCount > 0:
          // Clear the store when models are removed
          store.ClearAndSave();
          break;
        case > 0 when _priorModelCount == 0:
          // Load state when models are added
          store.ReloadState();
          break;
      }

      if (commands != null)
      {
        await commands.NotifyDocumentChanged();
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

  private void UnsubscribeFromModelEvents(NAV.Document document)
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
