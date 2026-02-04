using Microsoft.Extensions.DependencyInjection;
using Speckle.Connector.Navisworks.Bindings;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connector.Navisworks.HostApp;

/// <summary>
/// Manages document and model state change notifications for the Navisworks connector.
/// Coalesces various document events into batched updates to be processed during idle time.
/// </summary>
public sealed class NavisworksDocumentEvents
{
  private readonly IServiceProvider _serviceProvider;
  private readonly ITopLevelExceptionHandler _topLevelExceptionHandler;
  private readonly IAppIdleManager _idleManager;
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
        activeDocument.Models.CollectionChanged += HandleDocumentModelCountChanged;
        activeDocument.Models.CollectionChanging += HandleDocumentModelCountChanging;
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

    _topLevelExceptionHandler.CatchUnhandled(
      () =>
        _idleManager.SubscribeToIdle(
          nameof(ProcessModelStateChangeAsync),
          async () => await ProcessModelStateChangeAsync()
        )
    );
  }

  private async Task ProcessModelStateChangeAsync()
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

  private void UnsubscribeFromModelEvents(NAV.Document document)
  {
    document.Models.CollectionChanged -= HandleDocumentModelCountChanged;
    document.Models.CollectionChanging -= HandleDocumentModelCountChanging;

    var sendBinding = _serviceProvider
      .GetRequiredService<IEnumerable<IBinding>>()
      .OfType<NavisworksSendBinding>()
      .First();
    sendBinding.CancelAllSendOperations();
  }
}
