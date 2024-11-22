using Autodesk.Navisworks.Api;
using Speckle.Connector.Navisworks.Bindings;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connector.Navisworks.HostApp;

/// <summary>
/// Manages document state change notifications for the Navisworks connector.
/// Coalesces various document events into batched UI updates using the idle manager.
/// </summary>
public class NavisworksDocumentEvents : IDisposable
{
  private readonly IBasicConnectorBinding _basicBinding;
  private readonly ITopLevelExceptionHandler _topLevelExceptionHandler;
  private readonly IAppIdleManager _idleManager;
  private bool _isSubscribed;
  private readonly object _subscriptionLock = new();
  private bool _disposed;

  /// <summary>
  /// Initializes event handling for document and model changes.
  /// </summary>
  public NavisworksDocumentEvents(
    IBasicConnectorBinding basicBinding,
    ITopLevelExceptionHandler topLevelExceptionHandler,
    IAppIdleManager idleManager
  )
  {
    _basicBinding = basicBinding;
    _topLevelExceptionHandler = topLevelExceptionHandler;
    _idleManager = idleManager;
    SubscribeToEvents();
  }

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

      NavisworksApp.ActiveDocumentChanged += OnDocumentEvent;
      NavisworksApp.DocumentAdded += OnDocumentEvent;
      NavisworksApp.DocumentRemoved += OnDocumentEvent;

      if (NavisworksApp.ActiveDocument != null && NavisworksApp.ActiveDocument.Models.Count > 0)
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
    var commands = (_basicBinding as NavisworksBasicConnectorBinding)?.Commands;
    if (commands != null)
    {
      await commands.NotifyDocumentChanged().ConfigureAwait(false);
    }
  }

  private void UnsubscribeFromEvents()
  {
    NavisworksApp.ActiveDocumentChanged -= OnDocumentEvent;
    NavisworksApp.DocumentAdded -= OnDocumentEvent;
    NavisworksApp.DocumentRemoved -= OnDocumentEvent;

    if (NavisworksApp.ActiveDocument != null && NavisworksApp.ActiveDocument.Models.Count > 0)
    {
      UnsubscribeFromModelEvents(NavisworksApp.ActiveDocument);
    }
  }

  private void UnsubscribeFromModelEvents(Document document) => document.Models.CollectionChanged -= OnDocumentEvent;

  public void Dispose()
  {
    Dispose(true);
    GC.SuppressFinalize(this);
  }

  protected virtual void Dispose(bool disposing)
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
