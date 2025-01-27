using Microsoft.Extensions.DependencyInjection;
using Speckle.Connector.Navisworks.Bindings;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Eventing;

namespace Speckle.Connector.Navisworks.HostApp;

/// <summary>
/// Manages document and model state change notifications for the Navisworks connector.
/// Coalesces various document events into batched updates to be processed during idle time.
/// </summary>
public sealed class NavisworksDocumentEvents
{
  private readonly IServiceProvider _serviceProvider;
  private readonly IEventAggregator _eventAggregator;
  private readonly object _subscriptionLock = new();

  private bool _isSubscribed;
  private bool _isProcessing;

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

    _eventAggregator.GetEvent<ActiveDocumentChangingEvent>().Subscribe(UnsubscribeFromDocumentModelEvents);
    _eventAggregator.GetEvent<ActiveDocumentChangedEvent>().Subscribe(SubscribeToDocumentModelEvents);
    _eventAggregator.GetEvent<CollectionChangingEvent>().Subscribe(HandleDocumentModelCountChanging);
    _eventAggregator.GetEvent<CollectionChangedEvent>().Subscribe(HandleDocumentModelCountChanged);
  }

  /// <summary>
  /// Subscribes to document-level events to monitor model state changes.
  /// </summary>
  private void SubscribeToDocumentModelEvents(object _)
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
        activeDocument.Models.CollectionChanged += OnCollectionChanged;
        activeDocument.Models.CollectionChanging += OnCollectionChanging;

      }

      _isSubscribed = true;
    }
  }

  private async void OnCollectionChanged(object sender, EventArgs _) => await _eventAggregator.GetEvent<CollectionChangedEvent>().PublishAsync(sender);

  private async void OnCollectionChanging(object sender, EventArgs _) => await _eventAggregator.GetEvent<CollectionChangingEvent>().PublishAsync(sender);

  /// <summary>
  /// Tracks the current model count before changes occur.
  /// </summary>
  private void HandleDocumentModelCountChanging(object sender) =>
    _priorModelCount = ((NAV.Document)sender).Models.Count;

  /// <summary>
  /// Schedules processing of model count changes during idle time.
  /// </summary>
  private void HandleDocumentModelCountChanged(object sender)
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

  private void UnsubscribeFromDocumentModelEvents(object _)
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
    document.Models.CollectionChanged -= OnCollectionChanged;
    document.Models.CollectionChanging -= OnCollectionChanging;

    var sendBinding = _serviceProvider.GetRequiredService<NavisworksSendBinding>();
    sendBinding.CancelAllSendOperations();
  }

}
