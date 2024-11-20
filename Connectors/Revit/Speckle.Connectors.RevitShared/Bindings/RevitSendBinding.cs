using System.Collections.Concurrent;
using Autodesk.Revit.DB;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Cancellation;
using Speckle.Connectors.Common.Operations;
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
  private readonly IRevitIdleManager _idleManager;
  private readonly APIContext _apiContext;
  private readonly CancellationManager _cancellationManager;
  private readonly IServiceProvider _serviceProvider;
  private readonly ISendConversionCache _sendConversionCache;
  private readonly IOperationProgressManager _operationProgressManager;
  private readonly ToSpeckleSettingsManager _toSpeckleSettingsManager;
  private readonly ILogger<RevitSendBinding> _logger;
  private readonly ElementUnpacker _elementUnpacker;
  private readonly IRevitConversionSettingsFactory _revitConversionSettingsFactory;
  private readonly ISpeckleApplication _speckleApplication;

  /// <summary>
  /// Used internally to aggregate the changed objects' id. Note we're using a concurrent dictionary here as the expiry check method is not thread safe, and this was causing problems. See:
  /// [CNX-202: Unhandled Exception Occurred when receiving in Rhino](https://linear.app/speckle/issue/CNX-202/unhandled-exception-occurred-when-receiving-in-rhino)
  /// As to why a concurrent dictionary, it's because it's the cheapest/easiest way to do so.
  /// https://stackoverflow.com/questions/18922985/concurrent-hashsett-in-net-framework
  /// </summary>
  private ConcurrentDictionary<ElementId, byte> ChangedObjectIds { get; set; } = new();

  public RevitSendBinding(
    IRevitIdleManager idleManager,
    RevitContext revitContext,
    APIContext apiContext,
    DocumentModelStore store,
    CancellationManager cancellationManager,
    IBrowserBridge bridge,
    IServiceProvider serviceProvider,
    ISendConversionCache sendConversionCache,
    IOperationProgressManager operationProgressManager,
    ToSpeckleSettingsManager toSpeckleSettingsManager,
    ILogger<RevitSendBinding> logger,
    ElementUnpacker elementUnpacker,
    IRevitConversionSettingsFactory revitConversionSettingsFactory,
    ISpeckleApplication speckleApplication
  )
    : base("sendBinding", store, bridge, revitContext)
  {
    _idleManager = idleManager;
    _apiContext = apiContext;
    _cancellationManager = cancellationManager;
    _serviceProvider = serviceProvider;
    _sendConversionCache = sendConversionCache;
    _operationProgressManager = operationProgressManager;
    _toSpeckleSettingsManager = toSpeckleSettingsManager;
    _logger = logger;
    _elementUnpacker = elementUnpacker;
    _revitConversionSettingsFactory = revitConversionSettingsFactory;
    _speckleApplication = speckleApplication;
    var topLevelExceptionHandler = Parent.TopLevelExceptionHandler;

    Commands = new SendBindingUICommands(bridge);
    // TODO expiry events
    // TODO filters need refresh events
    _idleManager.RunAsync(() =>
    {
      revitContext.UIApplication.NotNull().Application.DocumentChanged += (_, e) =>
        topLevelExceptionHandler.CatchUnhandled(() => DocChangeHandler(e));
    });
    Store.DocumentChanged += (_, _) =>
      topLevelExceptionHandler.FireAndForget(async () => await OnDocumentChanged().ConfigureAwait(false));
  }

  public List<ISendFilter> GetSendFilters() =>
    [
      new RevitSelectionFilter() { IsDefault = true },
      new RevitViewsFilter(RevitContext, _apiContext),
      new RevitCategoriesFilter(RevitContext, _apiContext)
    ];

  public List<ICardSetting> GetSendSettings() =>
    [
      new DetailLevelSetting(DetailLevelType.Medium),
      new ReferencePointSetting(ReferencePointType.InternalOrigin),
      new SendParameterNullOrEmptyStringsSetting(false)
    ];

  public void CancelSend(string modelCardId) => _cancellationManager.CancelOperation(modelCardId);

  public SendBindingUICommands Commands { get; }

  public async Task Send(string modelCardId)
  {
    // Note: removed top level handling thing as it was confusing me
    try
    {
      if (Store.GetModelById(modelCardId) is not SenderModelCard modelCard)
      {
        // Handle as GLOBAL ERROR at BrowserBridge
        throw new InvalidOperationException("No publish model card was found.");
      }

      CancellationToken cancellationToken = _cancellationManager.InitCancellationTokenSource(modelCardId);

      using var scope = _serviceProvider.CreateScope();
      scope
        .ServiceProvider.GetRequiredService<IConverterSettingsStore<RevitConversionSettings>>()
        .Initialize(
          _revitConversionSettingsFactory.Create(
            _toSpeckleSettingsManager.GetDetailLevelSetting(modelCard),
            _toSpeckleSettingsManager.GetReferencePointSetting(modelCard),
            _toSpeckleSettingsManager.GetSendParameterNullOrEmptyStringsSetting(modelCard)
          )
        );

      List<Element> elements = await RefreshElementsOnSender(modelCard.NotNull()).ConfigureAwait(false);
      List<ElementId> elementIds = elements.Select(el => el.Id).ToList();

      if (elementIds.Count == 0)
      {
        // Handle as CARD ERROR in this function
        throw new SpeckleSendFilterException("No objects were found to convert. Please update your publish filter!");
      }

      var sendResult = await scope
        .ServiceProvider.GetRequiredService<SendOperation<ElementId>>()
        .Execute(
          elementIds,
          modelCard.GetSendInfo(_speckleApplication.Slug),
          _operationProgressManager.CreateOperationProgressEventHandler(Parent, modelCardId, cancellationToken),
          cancellationToken
        )
        .ConfigureAwait(false);

      await Commands
        .SetModelSendResult(modelCardId, sendResult.RootObjId, sendResult.ConversionResults)
        .ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
      // SWALLOW -> UI handles it immediately, so we do not need to handle anything for now!
      // Idea for later -> when cancel called, create promise from UI to solve it later with this catch block.
      // So have 3 state on UI -> Cancellation clicked -> Cancelling -> Cancelled
    }
    catch (Exception ex) when (!ex.IsFatal()) // UX reasons - we will report operation exceptions as model card error. We may change this later when we have more exception documentation
    {
      _logger.LogModelCardHandledError(ex);
      await Commands.SetModelError(modelCardId, ex).ConfigureAwait(false);
    }
  }

  private async Task<List<Element>> RefreshElementsOnSender(SenderModelCard modelCard)
  {
    var activeUIDoc =
      RevitContext.UIApplication?.ActiveUIDocument
      ?? throw new SpeckleException("Unable to retrieve active UI document");

    if (modelCard.SendFilter is IRevitSendFilter viewFilter)
    {
      viewFilter.SetContext(RevitContext, _apiContext);
    }

    var selectedObjects = await _apiContext
      .Run(_ => modelCard.SendFilter.NotNull().RefreshObjectIds())
      .ConfigureAwait(false);

    List<Element> elements = selectedObjects
      .Select(uid => activeUIDoc.Document.GetElement(uid))
      .Where(el => el is not null)
      .ToList();

    if (modelCard.SendFilter is not null && modelCard.SendFilter.IdMap is not null)
    {
      var newSelectedObjectIds = new List<string>();
      foreach (Element element in elements)
      {
        modelCard.SendFilter.IdMap[element.Id.ToString()] = element.UniqueId;
        newSelectedObjectIds.Add(element.UniqueId);
      }

      // We update the state on the UI SenderModelCard to prevent potential inconsistencies between hostApp IdMap in sendfilters.
      await Commands
        .SetFilterObjectIds(modelCard.ModelCardId.NotNull(), modelCard.SendFilter.IdMap, newSelectedObjectIds)
        .ConfigureAwait(false);
    }

    return elements;
  }

  /// <summary>
  /// Keeps track of the changed element ids as well as checks if any of them need to trigger
  /// a filter refresh (e.g., views being added).
  /// </summary>
  /// <param name="e"></param>
  private void DocChangeHandler(Autodesk.Revit.DB.Events.DocumentChangedEventArgs e)
  {
    ICollection<ElementId> addedElementIds = e.GetAddedElementIds();
    ICollection<ElementId> deletedElementIds = e.GetDeletedElementIds();
    ICollection<ElementId> modifiedElementIds = e.GetModifiedElementIds();

    foreach (ElementId elementId in addedElementIds)
    {
      ChangedObjectIds[elementId] = 1;
    }

    foreach (ElementId elementId in deletedElementIds)
    {
      ChangedObjectIds[elementId] = 1;
    }

    foreach (ElementId elementId in modifiedElementIds)
    {
      ChangedObjectIds[elementId] = 1;
    }

    if (addedElementIds.Count > 0)
    {
      _idleManager.SubscribeToIdle(nameof(PostSetObjectIds), PostSetObjectIds);
    }

    if (HaveUnitsChanged(e.GetDocument()))
    {
      var objectIds = new List<string>();
      foreach (var sender in Store.GetSenders().ToList())
      {
        if (sender.SendFilter is null)
        {
          continue;
        }

        var selectedObjects = sender.SendFilter.NotNull().SelectedObjectIds;
        objectIds.AddRange(selectedObjects);
      }
      var unpackedObjectIds = _elementUnpacker.GetUnpackedElementIds(objectIds.ToList());
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
    foreach (var sender in Store.GetSenders().ToList())
    {
      await RefreshElementsOnSender(sender).ConfigureAwait(false);
    }
  }

  /// <summary>
  /// Notifies ui if any filters need refreshing. Currently, this only applies for view filters.
  /// </summary>
  private async Task CheckFilterExpiration()
  {
    // NOTE: below code seems like more make sense in terms of performance but it causes unmanaged exception on Revit
    // using var viewCollector = new FilteredElementCollector(RevitContext.UIApplication?.ActiveUIDocument.Document);
    // var views = viewCollector.OfClass(typeof(View)).Cast<View>().Select(v => v.Id).ToList();
    // var intersection = ChangedObjectIds.Keys.Intersect(views).ToList();
    // if (intersection.Count != 0)
    // {
    //    await Commands.RefreshSendFilters().ConfigureAwait(false);
    // }

    if (ChangedObjectIds.Keys.Any(e => RevitContext.UIApplication?.ActiveUIDocument.Document.GetElement(e) is View))
    {
      await Commands.RefreshSendFilters().ConfigureAwait(false);
    }
  }

  private async Task RunExpirationChecks()
  {
    var senders = Store.GetSenders().ToList();
    // string[] objectIdsList = ChangedObjectIds.Keys.ToArray();
    var doc = RevitContext.UIApplication?.ActiveUIDocument.Document;

    if (doc == null)
    {
      return;
    }

    var objUniqueIds = new List<string>();

    foreach (var sender in senders)
    {
      // if (sender.SendFilter is null) // NOTE: RunExpirationChecks sometimes triggered unnecessarily before send and, we didn't set up yet IdMap, if so we do not need to deal with it
      // {
      //   continue;
      // }

      foreach (var changedElementId in ChangedObjectIds.Keys)
      {
        if (sender.SendFilter?.IdMap?.TryGetValue(changedElementId.ToString(), out var id) ?? false)
        {
          objUniqueIds.Add(id);
        }
      }
    }

    // foreach (var changedElementId in ChangedObjectIds.Keys.ToArray())
    // {
    //   foreach (var sender in senders)
    //   {
    //     if (sender.SendFilter.NotNull().IdMap is null)
    //     {
    //       continue;
    //     }
    //     if (sender.SendFilter.NotNull().IdMap.NotNull().ContainsKey(changedElementId.ToString()))
    //     {
    //       objUniqueIds.Add(sender.SendFilter.NotNull().IdMap.NotNull()[changedElementId.ToString()]);
    //     }
    //   }
    // }

    var unpackedObjectIds = _elementUnpacker.GetUnpackedElementIds(objUniqueIds);
    _sendConversionCache.EvictObjects(unpackedObjectIds);

    // Note: we're doing object selection and card expiry management by old school ids
    List<string> expiredSenderIds = new();
    foreach (SenderModelCard modelCard in senders)
    {
      if (modelCard.SendFilter is IRevitSendFilter viewFilter)
      {
        viewFilter.SetContext(RevitContext, _apiContext);
      }

      var selectedObjects = modelCard.SendFilter.NotNull().IdMap.NotNull().Values;
      var intersection = selectedObjects.Intersect(objUniqueIds).ToList();
      bool isExpired = intersection.Count != 0;
      if (isExpired)
      {
        expiredSenderIds.Add(modelCard.ModelCardId.NotNull());
      }
    }

    await Commands.SetModelsExpired(expiredSenderIds).ConfigureAwait(false);
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
      await Commands
        .SetGlobalNotification(
          ToastNotificationType.INFO,
          "Document Switch",
          "Operations cancelled because of document swap!"
        )
        .ConfigureAwait(false);
    }
  }
}
