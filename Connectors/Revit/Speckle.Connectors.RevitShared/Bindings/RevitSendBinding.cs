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
using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Sdk;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.Revit.Bindings;

internal sealed class RevitSendBinding : RevitBaseBinding, ISendBinding
{
  private readonly IRevitIdleManager _idleManager;
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
  private ConcurrentDictionary<string, byte> ChangedObjectIds { get; set; } = new();

  /// <summary>
  /// We need it to get UniqueId whenever it is not available i.e. GetDeletedElementIds returns ElementId and cannot find its Element to get UniqueId. We store them both just before send to remember later.
  /// </summary>
  private ConcurrentDictionary<string, string> IdMap { get; } = new();

  public RevitSendBinding(
    IRevitIdleManager idleManager,
    RevitContext revitContext,
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

  public List<ISendFilter> GetSendFilters()
  {
    return new List<ISendFilter> { new RevitSelectionFilter() { IsDefault = true } };
  }

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

      var activeUIDoc =
        RevitContext.UIApplication?.ActiveUIDocument
        ?? throw new SpeckleException("Unable to retrieve active UI document");

      List<Element> elements = modelCard
        .SendFilter.NotNull()
        .GetObjectIds()
        .Select(uid => activeUIDoc.Document.GetElement(uid))
        .Where(el => el is not null)
        .ToList();

      foreach (Element element in elements)
      {
        IdMap[element.Id.ToString()] = element.UniqueId;
      }

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
      ChangedObjectIds[elementId.ToString()] = 1;
    }

    foreach (ElementId elementId in deletedElementIds)
    {
      ChangedObjectIds[elementId.ToString()] = 1;
    }

    foreach (ElementId elementId in modifiedElementIds)
    {
      ChangedObjectIds[elementId.ToString()] = 1;
    }

    if (HaveUnitsChanged(e.GetDocument()))
    {
      var objectIds = Store.GetSenders().SelectMany(s => s.SendFilter != null ? s.SendFilter.GetObjectIds() : []);
      var unpackedObjectIds = _elementUnpacker.GetUnpackedElementIds(objectIds.ToList());
      _sendConversionCache.EvictObjects(unpackedObjectIds);
    }
    _idleManager.SubscribeToIdle(nameof(RevitSendBinding), RunExpirationChecks);
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

  private async Task RunExpirationChecks()
  {
    var senders = Store.GetSenders();
    // string[] objectIdsList = ChangedObjectIds.Keys.ToArray();
    var doc = RevitContext.UIApplication?.ActiveUIDocument.Document;

    if (doc == null)
    {
      return;
    }

    var objUniqueIds = new List<string>();
    foreach (string changedElementId in ChangedObjectIds.Keys.ToArray())
    {
      if (IdMap.TryGetValue(changedElementId, out var uniqueId))
      {
        objUniqueIds.Add(uniqueId);
      }
    }

    var unpackedObjectIds = _elementUnpacker.GetUnpackedElementIds(objUniqueIds);
    _sendConversionCache.EvictObjects(unpackedObjectIds);

    // Note: we're doing object selection and card expiry management by old school ids
    List<string> expiredSenderIds = new();
    foreach (SenderModelCard modelCard in senders)
    {
      var intersection = modelCard.SendFilter.NotNull().GetObjectIds().Intersect(objUniqueIds).ToList();
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
    IdMap.Clear();

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
