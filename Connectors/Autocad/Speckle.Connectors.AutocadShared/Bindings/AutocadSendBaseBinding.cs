using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Autocad.HostApp;
using Speckle.Connectors.Autocad.HostApp.Extensions;
using Speckle.Connectors.Autocad.Operations.Send;
using Speckle.Connectors.Autocad.Plugin;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Cancellation;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Eventing;
using Speckle.Connectors.DUI.Exceptions;
using Speckle.Connectors.DUI.Logging;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.Settings;
using Speckle.Sdk;
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
  private readonly IAutocadIdleManager _idleManager;
  private readonly List<ISendFilter> _sendFilters;
  private readonly CancellationManager _cancellationManager;
  private readonly IServiceProvider _serviceProvider;
  private readonly ISendConversionCache _sendConversionCache;
  private readonly IOperationProgressManager _operationProgressManager;
  private readonly ILogger<AutocadSendBinding> _logger;
  private readonly ISpeckleApplication _speckleApplication;
  private readonly IThreadContext _threadContext;
  private readonly IEventAggregator _eventAggregator;

  /// <summary>
  /// Used internally to aggregate the changed objects' id. Note we're using a concurrent dictionary here as the expiry check method is not thread safe, and this was causing problems. See:
  /// [CNX-202: Unhandled Exception Occurred when receiving in Rhino](https://linear.app/speckle/issue/CNX-202/unhandled-exception-occurred-when-receiving-in-rhino)
  /// As to why a concurrent dictionary, it's because it's the cheapest/easiest way to do so.
  /// https://stackoverflow.com/questions/18922985/concurrent-hashsett-in-net-framework
  /// </summary>
  private ConcurrentDictionary<string, byte> ChangedObjectIds { get; set; } = new();

  protected AutocadSendBaseBinding(
    DocumentModelStore store,
    IAutocadIdleManager idleManager,
    IBrowserBridge parent,
    IEnumerable<ISendFilter> sendFilters,
    CancellationManager cancellationManager,
    IServiceProvider serviceProvider,
    ISendConversionCache sendConversionCache,
    IOperationProgressManager operationProgressManager,
    ILogger<AutocadSendBinding> logger,
    ISpeckleApplication speckleApplication,
    IThreadContext threadContext,
    IEventAggregator eventAggregator
  )
  {
    _store = store;
    _idleManager = idleManager;
    _serviceProvider = serviceProvider;
    _cancellationManager = cancellationManager;
    _sendFilters = sendFilters.ToList();
    _sendConversionCache = sendConversionCache;
    _operationProgressManager = operationProgressManager;
    _logger = logger;
    _speckleApplication = speckleApplication;
    _threadContext = threadContext;
    _eventAggregator = eventAggregator;
    Parent = parent;
    Commands = new SendBindingUICommands(parent);


    if (Application.DocumentManager.CurrentDocument != null)
    {
      // catches the case when autocad just opens up with a blank new doc
      TryRegisterSubscribeToObjectChanges(Application.DocumentManager.CurrentDocument);
    }
    // Since ids of the objects generates from same seed, we should clear the cache always whenever doc swapped.

    eventAggregator.GetEvent<DocumentActivatedEvent>().Subscribe(SubscribeToObjectChanges);
    eventAggregator.GetEvent<DocumentStoreChangedEvent>().Subscribe(OnDocumentStoreChangedEvent);
    eventAggregator.GetEvent<DocumentToBeDestroyedEvent>().Subscribe(OnDocumentDestroyed);
    eventAggregator.GetEvent<ObjectAppendedEvent>().Subscribe(OnObjectAppended);
    eventAggregator.GetEvent<ObjectErasedEvent>().Subscribe(ObjectErased);
    eventAggregator.GetEvent<ObjectModifiedEvent>().Subscribe(ObjectModified);
  }
  private void OnDocumentDestroyed(DocumentCollectionEventArgs args)
  {
    Document doc = args.Document;
    if (!_docSubsTracker.Contains(doc.Name))
    {
      doc.Database.ObjectAppended -= DatabaseOnObjectAppended;
      doc.Database.ObjectErased -=  DatabaseOnObjectErased;
      doc.Database.ObjectModified -= DatabaseObjectModified;
      
      _docSubsTracker.Remove(doc.Name);
    }
  }
  private void OnDocumentStoreChangedEvent(object _) => _sendConversionCache.ClearCache();

  private readonly List<string> _docSubsTracker = new();

 private void SubscribeToObjectChanges(DocumentCollectionEventArgs e) => TryRegisterSubscribeToObjectChanges(e.Document);
  private void TryRegisterSubscribeToObjectChanges(Document? doc)
  {
    if (doc == null || doc.Database == null || _docSubsTracker.Contains(doc.Name))
    {
      return;
    }

    _docSubsTracker.Add(doc.Name);
    doc.Database.ObjectAppended += DatabaseOnObjectAppended;
    doc.Database.ObjectErased +=  DatabaseOnObjectErased;
    doc.Database.ObjectModified += DatabaseObjectModified;
  }

  private async void DatabaseOnObjectAppended(object sender, ObjectEventArgs e) => await _eventAggregator.GetEvent<ObjectAppendedEvent>().PublishAsync(e);
  private async void DatabaseOnObjectErased(object sender, ObjectErasedEventArgs e) => await _eventAggregator.GetEvent<ObjectErasedEvent>().PublishAsync(e);
  private async void DatabaseObjectModified(object sender, ObjectEventArgs e) => await _eventAggregator.GetEvent<ObjectModifiedEvent>().PublishAsync(e);

  private void OnObjectAppended(ObjectEventArgs e) => OnChangeChangedObjectIds(e.DBObject);
  private void ObjectErased(ObjectErasedEventArgs e) => OnChangeChangedObjectIds(e.DBObject);
  private void ObjectModified(ObjectEventArgs e) => OnChangeChangedObjectIds(e.DBObject);

  private void OnChangeChangedObjectIds(DBObject dBObject)
  {
    ChangedObjectIds[dBObject.GetSpeckleApplicationId()] = 1;
    _idleManager.SubscribeToIdle(nameof(AutocadSendBinding), async () => await RunExpirationChecks());
  }

  private async Task RunExpirationChecks()
  {
    var senders = _store.GetSenders();
    string[] objectIdsList = ChangedObjectIds.Keys.ToArray();
    List<string> expiredSenderIds = new();

    _sendConversionCache.EvictObjects(objectIdsList);

    foreach (SenderModelCard modelCard in senders)
    {
      var intersection = modelCard.SendFilter.NotNull().RefreshObjectIds().Intersect(objectIdsList).ToList();
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
    await _threadContext.RunOnWorkerAsync(async () => await SendInternal(modelCardId));

  protected abstract void InitializeSettings(IServiceProvider serviceProvider);

  private async Task SendInternal(string modelCardId)
  {
    try
    {
      if (_store.GetModelById(modelCardId) is not SenderModelCard modelCard)
      {
        // Handle as GLOBAL ERROR at BrowserBridge
        throw new InvalidOperationException("No publish model card was found.");
      }

      using var scope = _serviceProvider.CreateScope();
      InitializeSettings(scope.ServiceProvider);

      CancellationToken cancellationToken = _cancellationManager.InitCancellationTokenSource(modelCardId);

      // Disable document activation (document creation and document switch)
      // Not disabling results in DUI model card being out of sync with the active document
      // The DocumentActivated event isn't usable probably because it is pushed to back of main thread queue
      Application.DocumentManager.DocumentActivationEnabled = false;

      // Get elements to convert
      List<AutocadRootObject> autocadObjects = Application.DocumentManager.CurrentDocument.GetObjects(
        modelCard.SendFilter.NotNull().RefreshObjectIds()
      );

      if (autocadObjects.Count == 0)
      {
        // Handle as CARD ERROR in this function
        throw new SpeckleSendFilterException("No objects were found to convert. Please update your publish filter!");
      }

      var sendResult = await scope
        .ServiceProvider.GetRequiredService<SendOperation<AutocadRootObject>>()
        .Execute(
          autocadObjects,
          modelCard.GetSendInfo(_speckleApplication.Slug),
          _operationProgressManager.CreateOperationProgressEventHandler(Parent, modelCardId, cancellationToken),
          cancellationToken
        );

      await Commands.SetModelSendResult(modelCardId, sendResult.RootObjId, sendResult.ConversionResults);
    }
    catch (OperationCanceledException)
    {
      // SWALLOW -> UI handles it immediately, so we do not need to handle anything for now!
      // Idea for later -> when cancel called, create promise from UI to solve it later with this catch block.
      // So have 3 state on UI -> Cancellation clicked -> Cancelling -> Cancelled
      return;
    }
    catch (Exception ex) when (!ex.IsFatal()) // UX reasons - we will report operation exceptions as model card error. We may change this later when we have more exception documentation
    {
      _logger.LogModelCardHandledError(ex);
      await Commands.SetModelError(modelCardId, ex);
    }
    finally
    {
      // renable document activation
      Application.DocumentManager.DocumentActivationEnabled = true;
    }
  }

  public void CancelSend(string modelCardId) => _cancellationManager.CancelOperation(modelCardId);
}
