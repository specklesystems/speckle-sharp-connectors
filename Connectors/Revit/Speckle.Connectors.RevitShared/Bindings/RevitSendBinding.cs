using System.Collections.Concurrent;
using Autodesk.Revit.DB;
using Microsoft.Extensions.Logging;
using Speckle.Autofac.DependencyInjection;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Exceptions;
using Speckle.Connectors.DUI.Logging;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.Settings;
using Speckle.Connectors.Revit.Plugin;
using Speckle.Connectors.RevitShared;
using Speckle.Connectors.Utils.Caching;
using Speckle.Connectors.Utils.Cancellation;
using Speckle.Connectors.Utils.Operations;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Sdk;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.Revit.Bindings;

internal sealed class RevitSendBinding : RevitBaseBinding, ISendBinding
{
  private readonly IRevitIdleManager _idleManager;
  private readonly CancellationManager _cancellationManager;
  private readonly IUnitOfWorkFactory _unitOfWorkFactory;
  private readonly ISendConversionCache _sendConversionCache;
  private readonly IOperationProgressManager _operationProgressManager;
  private readonly ILogger<RevitSendBinding> _logger;

  /// <summary>
  /// Used internally to aggregate the changed objects' id. Note we're using a concurrent dictionary here as the expiry check method is not thread safe, and this was causing problems. See:
  /// [CNX-202: Unhandled Exception Occurred when receiving in Rhino](https://linear.app/speckle/issue/CNX-202/unhandled-exception-occurred-when-receiving-in-rhino)
  /// As to why a concurrent dictionary, it's because it's the cheapest/easiest way to do so.
  /// https://stackoverflow.com/questions/18922985/concurrent-hashsett-in-net-framework
  /// </summary>
  private ConcurrentDictionary<string, byte> ChangedObjectIds { get; set; } = new();

  public RevitSendBinding(
    IRevitIdleManager idleManager,
    RevitContext revitContext,
    DocumentModelStore store,
    CancellationManager cancellationManager,
    IBridge bridge,
    IUnitOfWorkFactory unitOfWorkFactory,
    ISendConversionCache sendConversionCache,
    IOperationProgressManager operationProgressManager,
    ILogger<RevitSendBinding> logger
  )
    : base("sendBinding", store, bridge, revitContext)
  {
    _idleManager = idleManager;
    _cancellationManager = cancellationManager;
    _unitOfWorkFactory = unitOfWorkFactory;
    _sendConversionCache = sendConversionCache;
    _operationProgressManager = operationProgressManager;
    _logger = logger;
    var topLevelExceptionHandler = Parent.TopLevelExceptionHandler;

    Commands = new SendBindingUICommands(bridge);
    // TODO expiry events
    // TODO filters need refresh events
    _idleManager.RunAsync(() =>
    {
      revitContext.UIApplication.NotNull().Application.DocumentChanged += (_, e) =>
        topLevelExceptionHandler.CatchUnhandled(() => DocChangeHandler(e));
    });
    Store.DocumentChanged += (_, _) => topLevelExceptionHandler.CatchUnhandled(OnDocumentChanged);
  }

  public List<ISendFilter> GetSendFilters()
  {
    return new List<ISendFilter> { new RevitSelectionFilter() { IsDefault = true } };
  }

  public List<CardSetting> GetSendSettings() =>
    new()
    {
      new()
      {
        Id = "modelOrigin",
        Title = "Model Origin",
        Type = "string",
        Enum = ["Internal Origin", "Project Base", "Survey"],
        Value = "Internal Origin"
      },
      new()
      {
        Id = "geometryFidelity",
        Title = "Geometry Fidelity",
        Type = "string",
        Enum = ["Coarse", "Medium", "Fine"],
        Value = "Coarse"
      },
    };

  public void CancelSend(string modelCardId)
  {
    _cancellationManager.CancelOperation(modelCardId);
  }

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

      // POC: probably the CTS SHOULD be injected as InstancePerLifetimeScope and then
      // it can be injected where needed instead of passing it around like a bomb :D
      CancellationTokenSource cts = _cancellationManager.InitCancellationTokenSource(modelCardId);

      using IUnitOfWork<SendOperation<ElementId>> sendOperation = _unitOfWorkFactory.Resolve<
        SendOperation<ElementId>
      >();

      List<ElementId> revitObjects = modelCard
        .SendFilter.NotNull()
        .GetObjectIds()
        .Select(ElementIdHelper.Parse)
        .ToList();

      if (revitObjects.Count == 0)
      {
        // Handle as CARD ERROR in this function
        throw new SpeckleSendFilterException("No objects were found to convert. Please update your publish filter!");
      }

      var sendResult = await sendOperation
        .Service.Execute(
          revitObjects,
          modelCard.GetSendInfo(Speckle.Connectors.Utils.Connector.Slug),
          (status, progress) =>
            _operationProgressManager.SetModelProgress(
              Parent,
              modelCardId,
              new ModelCardProgress(modelCardId, status, progress),
              cts
            ),
          cts.Token
        )
        .ConfigureAwait(false);

      Commands.SetModelSendResult(modelCardId, sendResult.RootObjId, sendResult.ConversionResults);
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
      Commands.SetModelError(modelCardId, ex);
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

    // TODO: CHECK IF ANY OF THE ABOVE ELEMENTS NEED TO TRIGGER A FILTER REFRESH
    _idleManager.SubscribeToIdle(nameof(RevitSendBinding), RunExpirationChecks);
  }

  private void RunExpirationChecks()
  {
    var senders = Store.GetSenders();
    string[] objectIdsList = ChangedObjectIds.Keys.ToArray();
    List<string> expiredSenderIds = new();

    _sendConversionCache.EvictObjects(objectIdsList);

    foreach (SenderModelCard modelCard in senders)
    {
      var intersection = modelCard.SendFilter.NotNull().GetObjectIds().Intersect(objectIdsList).ToList();
      bool isExpired = intersection.Count != 0;
      if (isExpired)
      {
        expiredSenderIds.Add(modelCard.ModelCardId.NotNull());
      }
    }

    Commands.SetModelsExpired(expiredSenderIds);
    ChangedObjectIds = new();
  }

  // POC: Will be re-addressed later with better UX with host apps that are friendly on async doc operations.
  // That's why don't bother for now how to get rid of from dup logic in other bindings.
  private void OnDocumentChanged()
  {
    if (_cancellationManager.NumberOfOperations > 0)
    {
      _cancellationManager.CancelAllOperations();
      Commands.SetGlobalNotification(
        ToastNotificationType.INFO,
        "Document Switch",
        "Operations cancelled because of document swap!"
      );
    }
  }
}
