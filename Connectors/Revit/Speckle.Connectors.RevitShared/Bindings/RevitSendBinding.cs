using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Cancellation;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Exceptions;
using Speckle.Connectors.DUI.Logging;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.Settings;
using Speckle.Connectors.Revit.HostApp;
using Speckle.Connectors.Revit.Operations.Send.Settings;
using Speckle.Connectors.Revit.Plugin;
using Speckle.Connectors.RevitShared.Operations.Send.Filters;
using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Sdk;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.Revit.Bindings;

internal sealed class RevitSendBinding : RevitBaseBinding, ISendBinding
{
  private readonly IAppIdleManager _idleManager;
  private readonly RevitContext _revitContext;
  private readonly DocumentModelStore _store;
  private readonly ICancellationManager _cancellationManager;
  private readonly IServiceProvider _serviceProvider;
  private readonly ISendConversionCache _sendConversionCache;
  private readonly IOperationProgressManager _operationProgressManager;
  private readonly ToSpeckleSettingsManager _toSpeckleSettingsManager;
  private readonly ILogger<RevitSendBinding> _logger;
  private readonly ElementUnpacker _elementUnpacker;
  private readonly IRevitConversionSettingsFactory _revitConversionSettingsFactory;
  private readonly ISpeckleApplication _speckleApplication;
  private readonly ITopLevelExceptionHandler _topLevelExceptionHandler;
  private readonly LinkedModelHandler _linkedModelHandler;
  private readonly IThreadContext _threadContext;

  /// <summary>
  /// Used internally to aggregate the changed objects' id. Note we're using a concurrent dictionary here as the expiry check method is not thread safe, and this was causing problems. See:
  /// [CNX-202: Unhandled Exception Occurred when receiving in Rhino](https://linear.app/speckle/issue/CNX-202/unhandled-exception-occurred-when-receiving-in-rhino)
  /// As to why a concurrent dictionary, it's because it's the cheapest/easiest way to do so.
  /// https://stackoverflow.com/questions/18922985/concurrent-hashsett-in-net-framework
  /// </summary>
  private ConcurrentHashSet<ElementId> ChangedObjectIds { get; set; } = new();

  public RevitSendBinding(
    IAppIdleManager idleManager,
    RevitContext revitContext,
    DocumentModelStore store,
    ICancellationManager cancellationManager,
    IBrowserBridge bridge,
    IServiceProvider serviceProvider,
    ISendConversionCache sendConversionCache,
    IOperationProgressManager operationProgressManager,
    ToSpeckleSettingsManager toSpeckleSettingsManager,
    ILogger<RevitSendBinding> logger,
    ElementUnpacker elementUnpacker,
    IRevitConversionSettingsFactory revitConversionSettingsFactory,
    ISpeckleApplication speckleApplication,
    ITopLevelExceptionHandler topLevelExceptionHandler,
    LinkedModelHandler linkedModelHandler,
    IThreadContext threadContext,
    IRevitTask revitTask
  )
    : base("sendBinding", bridge)
  {
    _idleManager = idleManager;
    _revitContext = revitContext;
    _store = store;
    _cancellationManager = cancellationManager;
    _serviceProvider = serviceProvider;
    _sendConversionCache = sendConversionCache;
    _operationProgressManager = operationProgressManager;
    _toSpeckleSettingsManager = toSpeckleSettingsManager;
    _logger = logger;
    _elementUnpacker = elementUnpacker;
    _revitConversionSettingsFactory = revitConversionSettingsFactory;
    _speckleApplication = speckleApplication;
    _topLevelExceptionHandler = topLevelExceptionHandler;
    _linkedModelHandler = linkedModelHandler;
    _threadContext = threadContext;

    Commands = new SendBindingUICommands(bridge);
    // TODO expiry events
    // TODO filters need refresh events

    revitTask.Run(() =>
    {
      revitContext.UIApplication.NotNull().Application.DocumentChanged += (_, e) =>
        _topLevelExceptionHandler.CatchUnhandled(() => DocChangeHandler(e));
      _store.DocumentChanged += (_, _) => topLevelExceptionHandler.FireAndForget(async () => await OnDocumentChanged());
    });
  }

  public List<ISendFilter> GetSendFilters() =>
    [
      new RevitSelectionFilter() { IsDefault = true },
      new RevitViewsFilter(_revitContext),
      new RevitCategoriesFilter(_revitContext)
    ];

  public List<ICardSetting> GetSendSettings() =>
    [
      new DetailLevelSetting(DetailLevelType.Medium),
      new ReferencePointSetting(ReferencePointType.InternalOrigin),
      new SendParameterNullOrEmptyStringsSetting(false),
      new LinkedModelsSetting(true),
      new SendRebarsAsVolumetricSetting(false)
    ];

  public void CancelSend(string modelCardId) => _cancellationManager.CancelOperation(modelCardId);

  public SendBindingUICommands Commands { get; }

  public async Task Send(string modelCardId)
  {
    // Note: removed top level handling thing as it was confusing me
    try
    {
      if (_store.GetModelById(modelCardId) is not SenderModelCard modelCard)
      {
        // Handle as GLOBAL ERROR at BrowserBridge
        throw new InvalidOperationException("No publish model card was found.");
      }

      using var cancellationItem = _cancellationManager.GetCancellationItem(modelCardId);

      using var scope = _serviceProvider.CreateScope();
      scope
        .ServiceProvider.GetRequiredService<IConverterSettingsStore<RevitConversionSettings>>()
        .Initialize(
          _revitConversionSettingsFactory.Create(
            _toSpeckleSettingsManager.GetDetailLevelSetting(modelCard),
            _toSpeckleSettingsManager.GetReferencePointSetting(modelCard),
            _toSpeckleSettingsManager.GetSendParameterNullOrEmptyStringsSetting(modelCard),
            _toSpeckleSettingsManager.GetLinkedModelsSetting(modelCard),
            _toSpeckleSettingsManager.GetSendRebarsAsVolumetric(modelCard)
          )
        );

      var elementsByTransform = await RefreshElementsIdsOnSender(modelCard.NotNull());

      if (elementsByTransform.Count == 0)
      {
        // Handle as CARD ERROR in this function
        throw new SpeckleSendFilterException("No objects were found to convert. Please update your publish filter!");
      }

      var sendResult = await scope
        .ServiceProvider.GetRequiredService<SendOperation<DocumentToConvert>>()
        .Execute(
          elementsByTransform,
          modelCard.GetSendInfo(_speckleApplication.ApplicationAndVersion),
          _operationProgressManager.CreateOperationProgressEventHandler(Parent, modelCardId, cancellationItem.Token),
          cancellationItem.Token
        );

      await Commands.SetModelSendResult(modelCardId, sendResult.VersionId, sendResult.ConversionResults);
    }
    catch (OperationCanceledException)
    {
      // SWALLOW -> UI handles it immediately, so we do not need to handle anything for now!
      // Idea for later -> when cancel called, create promise from UI to solve it later with this catch block.
      // So have 3 state on UI -> Cancellation clicked -> Cancelling -> Cancelled
    }
    catch (SpeckleRevitTaskException ex)
    {
      await SpeckleRevitTaskException.ProcessException(modelCardId, ex, _logger, Commands);
    }
    catch (Exception ex) when (!ex.IsFatal()) // UX reasons - we will report operation exceptions as model card error. We may change this later when we have more exception documentation
    {
      _logger.LogModelCardHandledError(ex);
      await Commands.SetModelError(modelCardId, ex);
    }
    finally
    {
      // otherwise the id of the operation persists on the cancellation manager and triggers 'Operations cancelled because of document swap!' message to UI.
      _cancellationManager.CancelOperation(modelCardId);
    }
  }

  private async Task<List<DocumentToConvert>> RefreshElementsIdsOnSender(SenderModelCard modelCard)
  {
    var activeUIDoc =
      _revitContext.UIApplication.NotNull().ActiveUIDocument
      ?? throw new SpeckleException("Unable to retrieve active UI document");

    if (modelCard.SendFilter.NotNull() is IRevitSendFilter viewFilter)
    {
      viewFilter.SetContext(_revitContext);
    }

    var selectedObjects = await _threadContext.RunOnMainAsync(
      () => Task.FromResult(modelCard.SendFilter.NotNull().RefreshObjectIds())
    );

    var allElements = selectedObjects
      .Select(uid => activeUIDoc.Document.GetElement(uid))
      .Where(el => el is not null)
      .ToList();

    // split elements between main model and linked models
    var elementsOnMainModel = allElements.Where(el => el is not RevitLinkInstance).ToList();
    var linkedModels = allElements.OfType<RevitLinkInstance>().ToList();

    // should ideally reuse the initialized value from the scoped IConverterSettingsStore<RevitConversionSettings>.
    // but, it's scoped and to avoid bigger scarier changes I'm re-fetching the setting here (inexpensive operation?)
    Transform? mainModelTransform = _toSpeckleSettingsManager.GetReferencePointSetting(modelCard);
    List<DocumentToConvert> documentElementContexts =
    [
      new(mainModelTransform, activeUIDoc.Document, elementsOnMainModel)
    ];

    // get the linked models setting - this decision belongs at this level
    bool includeLinkedModels = _toSpeckleSettingsManager.GetLinkedModelsSetting(modelCard);

    // ⚠️ process linked models - RevitSendBinding controls the flow based on settings!
    // If setting not enabled, we won't unpack (see if-else block)
    if (linkedModels.Count > 0)
    {
      var linkedDocumentContexts = new List<DocumentToConvert>();

      foreach (var linkedModel in linkedModels)
      {
        var linkedDoc = linkedModel.GetLinkDocument();
        if (linkedDoc == null)
        {
          continue;
        }

        // transform maps linked model elements into the main model's reference point coordinate system
        // first apply the user's reference point transform (setting) then adjust for the linked model's placement relative to host.
        Transform transform = (mainModelTransform ?? Transform.Identity).Multiply(
          linkedModel.GetTotalTransform().Inverse
        );

        // decision about whether to process elements is made here, not in the handler
        // only collects elements from linked models when the setting is enabled
        if (includeLinkedModels)
        {
          // handler is only responsible for element collection mechanics
          var linkedElements = _linkedModelHandler.GetLinkedModelElements(modelCard.SendFilter, linkedDoc, transform);
          linkedDocumentContexts.Add(new(transform, linkedDoc, linkedElements));
        }
        // ⚠️ when disabled, still adds empty contexts to maintain warning generation in RevitRootObjectBuilder
        // this approach (to signal that warnings are needed) relies on empty element lists which smells and is a bit of an implicit mechanism
        // buuuuut, it works (for now 👀).
        else
        {
          linkedDocumentContexts.Add(new(transform, linkedDoc, new List<Element>()));
        }
      }
      documentElementContexts.AddRange(linkedDocumentContexts);
    }

    // update ID map
    if (modelCard.SendFilter is not null && modelCard.SendFilter.IdMap is not null)
    {
      var newSelectedObjectIds = new List<string>();
      foreach (Element element in allElements)
      {
        modelCard.SendFilter.IdMap[element.Id.ToString()] = element.UniqueId;
        newSelectedObjectIds.Add(element.UniqueId);
      }

      // We update the state on the UI SenderModelCard to prevent potential inconsistencies between hostApp IdMap in sendfilters.
      await Commands.SetFilterObjectIds(
        modelCard.ModelCardId.NotNull(),
        modelCard.SendFilter.IdMap,
        newSelectedObjectIds
      );
    }

    return documentElementContexts;
  }

  /// <summary>
  /// Keeps track of the changed element ids as well as checks if any of them need to trigger
  /// a filter refresh (e.g., views being added).
  /// </summary>
  /// <param name="e"></param>
  private void DocChangeHandler(Autodesk.Revit.DB.Events.DocumentChangedEventArgs e)
  {
    ICollection<ElementId> modifiedElementIds = e.GetModifiedElementIds();

    // NOTE: Whenever we save data into file this event also trigger changes on its DataStorage.
    // On every add/remove/update model attempt triggers this handler and was causing unnecessary calls on `RunExpirationChecks`
    // Re-check it once we implement Linked Documents
    if (modifiedElementIds.Count == 1)
    {
      var doc = e.GetDocument();
      if (modifiedElementIds.All(el => doc.GetElement(el) is DataStorage))
      {
        return;
      }
    }

    ICollection<ElementId> addedElementIds = e.GetAddedElementIds();
    ICollection<ElementId> deletedElementIds = e.GetDeletedElementIds();

    foreach (ElementId elementId in addedElementIds)
    {
      ChangedObjectIds.Add(elementId);
    }

    foreach (ElementId elementId in deletedElementIds)
    {
      ChangedObjectIds.Add(elementId);
    }

    foreach (ElementId elementId in modifiedElementIds)
    {
      ChangedObjectIds.Add(elementId);
    }

    if (addedElementIds.Count > 0)
    {
      _idleManager.SubscribeToIdle(nameof(PostSetObjectIds), PostSetObjectIds);
    }

    if (HaveUnitsChanged(e.GetDocument()))
    {
      var objectIds = new List<string>();
      foreach (var sender in _store.GetSenders().ToList())
      {
        if (sender.SendFilter is null)
        {
          continue;
        }

        var selectedObjects = sender.SendFilter.NotNull().SelectedObjectIds;
        objectIds.AddRange(selectedObjects);
      }
      var unpackedObjectIds = _elementUnpacker.GetUnpackedElementIds(objectIds);
      _sendConversionCache.EvictObjects(unpackedObjectIds);
    }

    _idleManager.SubscribeToIdle(nameof(CheckFilterExpiration), CheckFilterExpiration);
    _idleManager.SubscribeToIdle(nameof(RunExpirationChecks), RunExpirationChecks);
  }

  // Keeps track of doc and current units
  private readonly Dictionary<string, string> _docUnitCache = new();

  private bool HaveUnitsChanged(Document doc)
  {
    var docId = doc.Title + doc.PathName;
    var unitSpecTypeIds = new List<ForgeTypeId>() // list of units we care about
    {
      SpecTypeId.Angle,
      SpecTypeId.Area,
      SpecTypeId.Distance,
      SpecTypeId.Length,
      SpecTypeId.Volume
    };
    var units = "";
    foreach (var typeId in unitSpecTypeIds)
    {
      units += doc.GetUnits().GetFormatOptions(typeId).GetUnitTypeId().TypeId;
    }

    if (_docUnitCache.TryGetValue(docId, out string? value))
    {
      if (value == units)
      {
        return false;
      }
      _docUnitCache[docId] = units;
      return true;
    }

    _docUnitCache[docId] = units;
    return false;
  }

  private async Task PostSetObjectIds()
  {
    foreach (var sender in _store.GetSenders().ToList())
    {
      await RefreshElementsIdsOnSender(sender);
    }
  }

  /// <summary>
  /// Notifies ui if any filters need refreshing. Currently, this only applies for view filters.
  /// </summary>
  private async Task CheckFilterExpiration()
  {
    // NOTE: below code seems like more make sense in terms of performance, but it causes unmanaged exception on Revit
    // using var viewCollector = new FilteredElementCollector(RevitContext.UIApplication?.ActiveUIDocument.Document);
    // var views = viewCollector.OfClass(typeof(View)).Cast<View>().Select(v => v.Id).ToList();
    // var intersection = ChangedObjectIds.Keys.Intersect(views).ToList();
    // if (intersection.Count != 0)
    // {
    //    await Commands.RefreshSendFilters();
    // }

    if (
      ChangedObjectIds.Any(e => _revitContext.UIApplication.NotNull().ActiveUIDocument.Document.GetElement(e) is View)
    )
    {
      await Commands.RefreshSendFilters();
    }
  }

  private async Task RunExpirationChecks()
  {
    var senders = _store.GetSenders().ToList();
    // string[] objectIdsList = ChangedObjectIds.Keys.ToArray();
    var doc = _revitContext.UIApplication.NotNull().ActiveUIDocument.Document;

    if (doc == null)
    {
      return;
    }

    var objUniqueIds = new List<string>();
    var changedIds = ChangedObjectIds.ToList();

    // Handling type changes: if an element's type is changed, we need to mark as changed all objects that have that type.
    // Step 1: get any changed types
    var elementTypeIdsList = changedIds
      .Select(e => doc.GetElement(e))
      .OfType<ElementType>()
      .Select(el => el.Id)
      .ToHashSet(); // ToHashSet() for faster Contains

    // Step 2: Find all elements of the changed types, and add them to the changed ids list.
    if (elementTypeIdsList.Count != 0)
    {
      using var collector = new FilteredElementCollector(doc);
      var collectorElements = collector
        .WhereElementIsNotElementType()
        .Where(e => elementTypeIdsList.Contains(e.GetTypeId()));
      foreach (var elm in collectorElements)
      {
        changedIds.Add(elm.Id);
      }
    }

    foreach (var sender in senders)
    {
      foreach (var changedElementId in changedIds)
      {
        if (sender.SendFilter?.IdMap?.TryGetValue(changedElementId.ToString(), out var id) ?? false)
        {
          objUniqueIds.Add(id);
        }
      }
    }

    var unpackedObjectIds = _elementUnpacker.GetUnpackedElementIds(objUniqueIds);
    _sendConversionCache.EvictObjects(unpackedObjectIds);

    // Note: we're doing object selection and card expiry management by old school ids
    List<string> expiredSenderIds = new();
    foreach (SenderModelCard modelCard in senders)
    {
      if (modelCard.SendFilter is IRevitSendFilter viewFilter)
      {
        viewFilter.SetContext(_revitContext);
      }

      if (modelCard.SendFilter is null || modelCard.SendFilter.IdMap is null)
      {
        continue;
      }

      var selectedObjects = modelCard.SendFilter.NotNull().IdMap.NotNull().Values;
      var intersection = selectedObjects.Intersect(objUniqueIds).ToList();
      bool isExpired = intersection.Count != 0;
      if (isExpired)
      {
        expiredSenderIds.Add(modelCard.ModelCardId.NotNull());
      }
    }

    await Commands.SetModelsExpired(expiredSenderIds);
    ChangedObjectIds = new();
  }

  // POC: Will be re-addressed later with better UX with host apps that are friendly on async doc operations.
  // That's why don't bother for now how to get rid of from dup logic in other bindings.
  private async Task OnDocumentChanged()
  {
    _sendConversionCache.ClearCache();

    if (_cancellationManager.NumberOfOperations > 0)
    {
      _cancellationManager.CancelAllOperations();
      await Commands.SetGlobalNotification(
        ToastNotificationType.INFO,
        "Document Switch",
        "Operations cancelled because of document swap!"
      );
    }
  }
}
