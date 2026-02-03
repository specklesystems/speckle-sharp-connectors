using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Autodesk.AutoCAD.DatabaseServices;
using Speckle.Connectors.Autocad.HostApp.Extensions;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Cancellation;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.Settings;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.Autocad.Bindings;

[SuppressMessage("ReSharper", "AsyncVoidMethod")]
public abstract class AutocadSendBaseBinding : ISendBinding
{
  public string Name => "sendBinding";
  public SendBindingUICommands Commands { get; }
  private OperationProgressManager OperationProgressManager { get; }
  public IBrowserBridge Parent { get; }

  private readonly DocumentModelStore _store;
  private readonly List<ISendFilter> _sendFilters;
  private readonly ICancellationManager _cancellationManager;
  private readonly ISendConversionCache _sendConversionCache;
  private readonly IThreadContext _threadContext;
  private readonly ITopLevelExceptionHandler _topLevelExceptionHandler;
  private readonly IAppIdleManager _idleManager;
  private readonly ISendOperationManagerFactory _sendOperationManagerFactory;

  /// <summary>
  /// Used internally to aggregate the changed objects' id. Note we're using a concurrent dictionary here as the expiry check method is not thread safe, and this was causing problems. See:
  /// [CNX-202: Unhandled Exception Occurred when receiving in Rhino](https://linear.app/speckle/issue/CNX-202/unhandled-exception-occurred-when-receiving-in-rhino)
  /// As to why a concurrent dictionary, it's because it's the cheapest/easiest way to do so.
  /// https://stackoverflow.com/questions/18922985/concurrent-hashsett-in-net-framework
  /// </summary>
  private ConcurrentBag<string> ChangedObjectIds { get; set; } = new();

  protected AutocadSendBaseBinding(
    DocumentModelStore store,
    IBrowserBridge parent,
    IEnumerable<ISendFilter> sendFilters,
    ICancellationManager cancellationManager,
    ISendConversionCache sendConversionCache,
    IThreadContext threadContext,
    ITopLevelExceptionHandler topLevelExceptionHandler,
    IAppIdleManager idleManager,
    ISendOperationManagerFactory sendOperationManagerFactory
  )
  {
    _store = store;
    _cancellationManager = cancellationManager;
    _sendFilters = sendFilters.ToList();
    _sendConversionCache = sendConversionCache;
    _threadContext = threadContext;
    _topLevelExceptionHandler = topLevelExceptionHandler;
    _idleManager = idleManager;
    _sendOperationManagerFactory = sendOperationManagerFactory;
    Parent = parent;
    Commands = new SendBindingUICommands(parent);

    Application.DocumentManager.DocumentActivated += (_, args) =>
      _topLevelExceptionHandler.CatchUnhandled(() => SubscribeToObjectChanges(args.Document));

    if (Application.DocumentManager.CurrentDocument != null)
    {
      // catches the case when autocad just opens up with a blank new doc
      SubscribeToObjectChanges(Application.DocumentManager.CurrentDocument);
    }
    // Since ids of the objects generates from same seed, we should clear the cache always whenever doc swapped.
    _store.DocumentChanged += (_, _) =>
    {
      _sendConversionCache.ClearCache();
    };
  }

  private readonly List<string> _docSubsTracker = new();

  private void SubscribeToObjectChanges(Document doc)
  {
    if (doc == null || doc.Database == null || _docSubsTracker.Contains(doc.Name))
    {
      return;
    }

    _docSubsTracker.Add(doc.Name);
    doc.Database.ObjectAppended += (_, e) => OnObjectChanged(e.DBObject);
    doc.Database.ObjectErased += (_, e) => OnObjectChanged(e.DBObject);
    doc.Database.ObjectModified += (_, e) => OnObjectChanged(e.DBObject);
  }

  private void OnObjectChanged(DBObject dbObject) =>
    _topLevelExceptionHandler.CatchUnhandled(() => OnChangeChangedObjectIds(dbObject));

  private void OnChangeChangedObjectIds(DBObject dBObject)
  {
    ChangedObjectIds.Add(dBObject.GetSpeckleApplicationId());
    _idleManager.SubscribeToIdle(nameof(RunExpirationChecks), async () => await RunExpirationChecks());
  }

  private async Task RunExpirationChecks()
  {
    var senders = _store.GetSenders();
    List<string> expiredSenderIds = new();

    _sendConversionCache.EvictObjects(ChangedObjectIds);

    foreach (SenderModelCard modelCard in senders)
    {
      var intersection = modelCard.SendFilter.NotNull().RefreshObjectIds().Intersect(ChangedObjectIds).ToList();
      bool isExpired = intersection.Count != 0;
      if (isExpired)
      {
        expiredSenderIds.Add(modelCard.ModelCardId.NotNull());
      }
    }

    await Commands.SetModelsExpired(expiredSenderIds);
    ChangedObjectIds = new();
  }

  public List<ISendFilter> GetSendFilters() => _sendFilters;

  public List<ICardSetting> GetSendSettings() => [];

  public async Task Send(string modelCardId) =>
    await _threadContext.RunOnMainAsync(async () => await SendInternal(modelCardId));

  protected abstract void InitializeSettings(IServiceProvider serviceProvider);

  private async Task SendInternal(string modelCardId)
  {
    try
    {
      using var manager = _sendOperationManagerFactory.Create();
      // Disable document activation (document creation and document switch)
      // Not disabling results in DUI model card being out of sync with the active document
      // The DocumentActivated event isn't usable probably because it is pushed to back of main thread queue
      Application.DocumentManager.DocumentActivationEnabled = false;
      await manager.Process(
        Commands,
        modelCardId,
        (sp, card) => InitializeSettings(sp),
        card => Application.DocumentManager.CurrentDocument.GetObjects(card.SendFilter.NotNull().RefreshObjectIds()),
        Application.DocumentManager.CurrentDocument.Name,
        null
      );
    }
    finally
    {
      // renable document activation
      Application.DocumentManager.DocumentActivationEnabled = true;
    }
  }

  public void CancelSend(string modelCardId) => _cancellationManager.CancelOperation(modelCardId);
}
