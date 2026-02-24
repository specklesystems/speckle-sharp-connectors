using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Cancellation;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
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
  private readonly RevitIdleManager _revitIdleManager;
  private readonly RevitContext _revitContext;
  private readonly DocumentModelStore _store;
  private readonly ICancellationManager _cancellationManager;
  private readonly ISendConversionCache _sendConversionCache;

  private readonly ToSpeckleSettingsManager _toSpeckleSettingsManager;
  private readonly ElementUnpacker _elementUnpacker;
  private readonly IRevitConversionSettingsFactory _revitConversionSettingsFactory;
  private readonly RevitToSpeckleCacheSingleton _revitToSpeckleCacheSingleton;
  private readonly ITopLevelExceptionHandler _topLevelExceptionHandler;
  private readonly LinkedModelHandler _linkedModelHandler;
  private readonly IThreadContext _threadContext;
  private readonly ISendOperationManagerFactory _sendOperationManagerFactory;
  private readonly ParameterUpdater _parameterUpdater;
  private bool _isDocChangedSubscribed;
  private EventHandler<Autodesk.Revit.DB.Events.DocumentChangedEventArgs>? _documentChangedHandler;
  private readonly ConnectorConfig _config;

  /// <summary>
  /// Used internally to aggregate the changed objects' id. Note we're using a concurrent dictionary here as the expiry check method is not thread safe, and this was causing problems. See:
  /// [CNX-202: Unhandled Exception Occurred when receiving in Rhino](https://linear.app/speckle/issue/CNX-202/unhandled-exception-occurred-when-receiving-in-rhino)
  /// As to why a concurrent dictionary, it's because it's the cheapest/easiest way to do so.
  /// https://stackoverflow.com/questions/18922985/concurrent-hashsett-in-net-framework
  /// </summary>
  private ConcurrentHashSet<ElementId> ChangedObjectIds { get; set; } = new();

  public RevitSendBinding(
    RevitIdleManager revitIdleManager,
    RevitContext revitContext,
    DocumentModelStore store,
    ICancellationManager cancellationManager,
    IBrowserBridge bridge,
    ISendConversionCache sendConversionCache,
    ToSpeckleSettingsManager toSpeckleSettingsManager,
    ElementUnpacker elementUnpacker,
    IRevitConversionSettingsFactory revitConversionSettingsFactory,
    RevitToSpeckleCacheSingleton revitToSpeckleCacheSingleton,
    ITopLevelExceptionHandler topLevelExceptionHandler,
    LinkedModelHandler linkedModelHandler,
    IThreadContext threadContext,
    IRevitTask revitTask,
    ISendOperationManagerFactory sendOperationManagerFactory,
    ParameterUpdater parameterUpdater,
    IConfigStore configStore
  )
    : base("sendBinding", bridge)
  {
    _revitIdleManager = revitIdleManager;
    _revitContext = revitContext;
    _store = store;
    _cancellationManager = cancellationManager;
    _sendConversionCache = sendConversionCache;
    _toSpeckleSettingsManager = toSpeckleSettingsManager;
    _elementUnpacker = elementUnpacker;
    _revitConversionSettingsFactory = revitConversionSettingsFactory;
    _revitToSpeckleCacheSingleton = revitToSpeckleCacheSingleton;
    _topLevelExceptionHandler = topLevelExceptionHandler;
    _linkedModelHandler = linkedModelHandler;
    _threadContext = threadContext;
    _sendOperationManagerFactory = sendOperationManagerFactory;
    _parameterUpdater = parameterUpdater;
    _config = configStore.GetConnectorConfig();

    Commands = new SendBindingUICommands(bridge);
    // TODO expiry events
    // TODO filters need refresh events

    revitTask.Run(() =>
    {
      // revitContext.UIApplication.NotNull().Application.DocumentChanged += (_, e) =>
      //   _topLevelExceptionHandler.CatchUnhandled(() => DocChangeHandler(e));
      _documentChangedHandler = (_, e) => _topLevelExceptionHandler.CatchUnhandled(() => DocChangeHandler(e));
      _store.ModelCardsChanged += (_, e) => OnModelCardsChanged(e);
      _store.DocumentChanged += (_, _) => topLevelExceptionHandler.FireAndForget(async () => await OnDocumentChanged());
    });
  }

  private void OnModelCardsChanged(ModelCardsChangedEventArgs e)
  {
    if (
      !_config.DocumentChangeListeningDisabled
      && e.ModelCards.Count > 0
      && e.ModelCards.Any(m => m.TypeDiscriminator == nameof(SenderModelCard))
    )
    {
      SubscribeDocChanged();
    }
    else
    {
      UnsubscribeDocChanged();
    }
  }

  private void SubscribeDocChanged()
  {
    if (_documentChangedHandler == null || _isDocChangedSubscribed)
    {
      return;
    }

    _threadContext.RunOnMain(() =>
    {
      _revitContext.UIApplication.NotNull().Application.DocumentChanged += _documentChangedHandler;
    });
    _isDocChangedSubscribed = true;
  }

  private void UnsubscribeDocChanged()
  {
    if (_documentChangedHandler == null || !_isDocChangedSubscribed)
    {
      return;
    }

    _threadContext.RunOnMain(() =>
    {
      _revitContext.UIApplication.NotNull().Application.DocumentChanged -= _documentChangedHandler;
    });
    _isDocChangedSubscribed = false;
  }

  public List<ISendFilter> GetSendFilters() =>
    [
      new RevitSelectionFilter { IsDefault = true },
      new RevitViewsFilter(_revitContext),
      new RevitCategoriesFilter(_revitContext)
    ];

  public List<ICardSetting> GetSendSettings() =>
    [
      new DetailLevelSetting(),
      new SendReferencePointSetting(),
      new SendParameterNullOrEmptyStringsSetting(),
      new LinkedModelsSetting(),
      new SendRebarsAsVolumetricSetting(),
      new SendAreasAsMeshSetting()
    ];

  public void CancelSend(string modelCardId) => _cancellationManager.CancelOperation(modelCardId);

  public SendBindingUICommands Commands { get; }

  public async Task Send(string modelCardId)
  {
    var document = _revitContext.UIApplication?.ActiveUIDocument?.Document;
    if (document == null)
    {
      throw new SpeckleException("No document is active for sending.");
    }
    using var manager = _sendOperationManagerFactory.Create();
    var (fileName, fileBytes) = GetFileInfo(document);
    await manager.Process<DocumentToConvert>(
      Commands,
      modelCardId,
      (sp, card) =>
      {
        sp.GetRequiredService<IConverterSettingsStore<RevitConversionSettings>>()
          .Initialize(
            _revitConversionSettingsFactory.Create(
              _toSpeckleSettingsManager.GetDetailLevelSetting(document, card),
              _toSpeckleSettingsManager.GetReferencePointSetting(document, card),
              _toSpeckleSettingsManager.GetSendParameterNullOrEmptyStringsSetting(document, card),
              _toSpeckleSettingsManager.GetLinkedModelsSetting(document, card),
              _toSpeckleSettingsManager.GetSendRebarsAsVolumetric(document, card),
              _toSpeckleSettingsManager.GetSendAreasAsMesh(document, card)
            )
          );
      },
      async x => await RefreshElementsIdsOnSender(document, x.NotNull()),
      fileName: fileName,
      fileSizeBytes: fileBytes
    );
  }

  public async Task UpdateParameters(List<ParameterChangeRequest> changes)
  {
    var document = _revitContext.UIApplication?.ActiveUIDocument?.Document;
    if (document == null)
    {
      throw new SpeckleException("No document is active.");
    }

    await _threadContext.RunOnMainAsync(() =>
    {
      using var transaction = new Transaction(document, "Speckle Parameter Updates");
      transaction.Start();

      foreach (var change in changes)
      {
        var element = document.GetElement(change.ApplicationId);
        if (element == null)
        {
          continue;
        }

        var path = ParsePath(change.Path);
        var result = _parameterUpdater.Update(element, path, change.To);
      }

      transaction.Commit();
      return Task.FromResult(true);
    });
  }

  private string[] ParsePath(string concatenatedPath)
  {
    // "properties.Parameters.Type Parameters.Other.Family Name"
    //  → ["Type Parameters", "Other", "Family Name"]
    var segments = concatenatedPath.Split('.');
    return segments.Skip(2).ToArray();
  }

  private static (string? fileName, long? fileBytes) GetFileInfo(Document document)
  {
    string fullPath = document.PathName;
    if (File.Exists(document.PathName))
    {
      var fileInfo = new FileInfo(document.PathName);
      return (fileInfo.Name, fileInfo.Length);
    }
    else
    {
      return (fullPath.Split('/').LastOrDefault(), null);
    }
  }

  private async Task<List<DocumentToConvert>> RefreshElementsIdsOnSender(Document document, SenderModelCard modelCard)
  {
    if (modelCard.SendFilter.NotNull() is IRevitSendFilter viewFilter)
    {
      viewFilter.SetContext(_revitContext);
    }

    var selectedObjects = await _threadContext.RunOnMainAsync(
      () => Task.FromResult(modelCard.SendFilter.NotNull().RefreshObjectIds())
    );

    var allElements = selectedObjects.Select(uid => document.GetElement(uid)).Where(el => el is not null).ToList();

    // split elements between main model and linked models
    var elementsOnMainModel = allElements.Where(el => el is not RevitLinkInstance).ToList();
    var linkedModels = allElements.OfType<RevitLinkInstance>().ToList();

    // should ideally reuse the initialized value from the scoped IConverterSettingsStore<RevitConversionSettings>.
    // but, it's scoped and to avoid bigger scarier changes I'm re-fetching the setting here (inexpensive operation?)
    Transform? mainModelTransform = _toSpeckleSettingsManager.GetReferencePointSetting(document, modelCard);
    List<DocumentToConvert> documentElementContexts = [new(mainModelTransform, document, elementsOnMainModel)];

    // get the linked models setting - this decision belongs at this level
    bool includeLinkedModels = _toSpeckleSettingsManager.GetLinkedModelsSetting(document, modelCard);

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
          var linkedElements = _linkedModelHandler.GetLinkedModelElements(
            document,
            modelCard.SendFilter,
            linkedDoc,
            transform
          );
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

      // NOTE: preserve & persist original user selection for selection filter implemented during
      // [CNX-2400](https://linear.app/speckle/issue/CNX-2400/object-dont-update-on-publish)
      // NOTE: update with current document for views and categories filter since these represent dynamic queries
      // View & categories filters self-update their SelectedObjectIds in RefreshObjectIds(), maintaining consistency
      var objectIds =
        modelCard.SendFilter is RevitSelectionFilter ? modelCard.SendFilter.SelectedObjectIds : newSelectedObjectIds;
      await Commands.SetFilterObjectIds(modelCard.ModelCardId.NotNull(), modelCard.SendFilter.IdMap, objectIds);
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
    var doc = e.GetDocument();
    if (doc == null)
    {
      return;
    }
    // NOTE: Whenever we save data into file this event also trigger changes on its DataStorage.
    // On every add/remove/update model attempt triggers this handler and was causing unnecessary calls on `RunExpirationChecks`
    // Re-check it once we implement Linked Documents
    if (modifiedElementIds.Count == 1)
    {
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
      _revitIdleManager.SubscribeToIdle(nameof(PostSetObjectIds), PostSetObjectIds);
    }

    if (HaveUnitsChanged(doc))
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
      var unpackedObjectIds = _elementUnpacker.GetUnpackedElementIds(objectIds, doc);
      _sendConversionCache.EvictObjects(unpackedObjectIds);
    }

    _revitIdleManager.SubscribeToIdle(nameof(CheckFilterExpiration), CheckFilterExpiration);
    _revitIdleManager.SubscribeToIdle(nameof(RunExpirationChecks), RunExpirationChecks);
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
    var document = _revitContext.UIApplication?.ActiveUIDocument?.Document;
    if (document == null)
    {
      return;
    }
    foreach (var sender in _store.GetSenders().ToList())
    {
      await RefreshElementsIdsOnSender(document, sender);
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
    var doc = _revitContext.UIApplication?.ActiveUIDocument?.Document;
    if (doc == null)
    {
      return;
    }

    if (ChangedObjectIds.Any(e => doc.GetElement(e) is View))
    {
      await Commands.RefreshSendFilters();
    }
  }

  private async Task RunExpirationChecks()
  {
    var senders = _store.GetSenders().ToList();
    // string[] objectIdsList = ChangedObjectIds.Keys.ToArray();
    var doc = _revitContext.UIApplication?.ActiveUIDocument?.Document;

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

    var unpackedObjectIds = _elementUnpacker.GetUnpackedElementIds(objUniqueIds, doc);
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
    _revitToSpeckleCacheSingleton.ClearCache();

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
