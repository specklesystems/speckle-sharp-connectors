using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Speckle.Autofac;
using Speckle.Autofac.DependencyInjection;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Exceptions;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.Rhino.HostApp;
using Speckle.Connectors.Utils.Caching;
using Speckle.Connectors.Utils.Cancellation;
using Speckle.Connectors.Utils.Operations;
using Speckle.Core.Common;

namespace Speckle.Connectors.Rhino.Bindings;

public sealed class RhinoSendBinding : ISendBinding
{
  public string Name => "sendBinding";
  public SendBindingUICommands Commands { get; }
  public IBridge Parent { get; }

  private readonly DocumentModelStore _store;
  private readonly IRhinoIdleManager _idleManager;
  private readonly IUnitOfWorkFactory _unitOfWorkFactory;
  private readonly List<ISendFilter> _sendFilters;
  private readonly CancellationManager _cancellationManager;
  private readonly RhinoSettings _rhinoSettings;
  private readonly ISendConversionCache _sendConversionCache;
  private readonly IOperationProgressManager _operationProgressManager;
  private readonly ITopLevelExceptionHandler _topLevelExceptionHandler;

  /// <summary>
  /// Used internally to aggregate the changed objects' id.
  /// </summary>
  private HashSet<string> ChangedObjectIds { get; set; } = new();

  public RhinoSendBinding(
    DocumentModelStore store,
    IRhinoIdleManager idleManager,
    IBridge parent,
    IEnumerable<ISendFilter> sendFilters,
    IUnitOfWorkFactory unitOfWorkFactory,
    RhinoSettings rhinoSettings,
    CancellationManager cancellationManager,
    ISendConversionCache sendConversionCache,
    IOperationProgressManager operationProgressManager
  )
  {
    _store = store;
    _idleManager = idleManager;
    _unitOfWorkFactory = unitOfWorkFactory;
    _sendFilters = sendFilters.ToList();
    _rhinoSettings = rhinoSettings;
    _cancellationManager = cancellationManager;
    _sendConversionCache = sendConversionCache;
    _operationProgressManager = operationProgressManager;
    _topLevelExceptionHandler = parent.TopLevelExceptionHandler.Parent.TopLevelExceptionHandler;
    Parent = parent;
    Commands = new SendBindingUICommands(parent); // POC: Commands are tightly coupled with their bindings, at least for now, saves us injecting a factory.
    SubscribeToRhinoEvents();
  }

  private void SubscribeToRhinoEvents()
  {
    RhinoDoc.LayerTableEvent += (_, _) =>
    {
      Commands.RefreshSendFilters();
    };

    Command.BeginCommand += (_, e) =>
    {
      if (e.CommandEnglishName == "BlockEdit")
      {
        var selectedObject = RhinoDoc.ActiveDoc.Objects.GetSelectedObjects(false, false).First();
        ChangedObjectIds.Add(selectedObject.Id.ToString());
      }
    };

    RhinoDoc.AddRhinoObject += (_, e) =>
      _topLevelExceptionHandler.CatchUnhandled(() =>
      {
        // NOTE: This does not work if rhino starts and opens a blank doc;
        if (!_store.IsDocumentInit)
        {
          return;
        }

        ChangedObjectIds.Add(e.ObjectId.ToString());
        _idleManager.SubscribeToIdle(nameof(RhinoSendBinding), RunExpirationChecks);
      });

    RhinoDoc.DeleteRhinoObject += (_, e) =>
      _topLevelExceptionHandler.CatchUnhandled(() =>
      {
        // NOTE: This does not work if rhino starts and opens a blank doc;
        if (!_store.IsDocumentInit)
        {
          return;
        }

        ChangedObjectIds.Add(e.ObjectId.ToString());
        _idleManager.SubscribeToIdle(nameof(RhinoSendBinding), RunExpirationChecks);
      });

    RhinoDoc.ReplaceRhinoObject += (_, e) =>
      _topLevelExceptionHandler.CatchUnhandled(() =>
      {
        // NOTE: This does not work if rhino starts and opens a blank doc;
        if (!_store.IsDocumentInit)
        {
          return;
        }

        ChangedObjectIds.Add(e.NewRhinoObject.Id.ToString());
        ChangedObjectIds.Add(e.OldRhinoObject.Id.ToString());
        _idleManager.SubscribeToIdle(nameof(RhinoSendBinding), RunExpirationChecks);
      });
  }

  public List<ISendFilter> GetSendFilters() => _sendFilters;

  public async Task Send(string modelCardId)
  {
    using var unitOfWork = _unitOfWorkFactory.Resolve<SendOperation<RhinoObject>>();
    try
    {
      if (_store.GetModelById(modelCardId) is not SenderModelCard modelCard)
      {
        // Handle as GLOBAL ERROR at BrowserBridge
        throw new InvalidOperationException("No publish model card was found.");
      }

      //  Init cancellation token source -> Manager also cancel it if exist before
      CancellationTokenSource cts = _cancellationManager.InitCancellationTokenSource(modelCardId);

      List<RhinoObject> rhinoObjects = modelCard
        .SendFilter.NotNull()
        .GetObjectIds()
        .Select(id => RhinoDoc.ActiveDoc.Objects.FindId(new Guid(id)))
        .Where(obj => obj != null)
        .ToList();

      if (rhinoObjects.Count == 0)
      {
        // Handle as CARD ERROR in this function
        throw new SpeckleSendFilterException("No objects were found to convert. Please update your publish filter!");
      }

      var sendResult = await unitOfWork
        .Service.Execute(
          rhinoObjects,
          modelCard.GetSendInfo(_rhinoSettings.HostAppInfo.Name),
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
    catch (Exception e) when (!e.IsFatal()) // UX reasons - we will report operation exceptions as model card error.
    {
      Commands.SetModelError(modelCardId, e);
    }
    catch (OperationCanceledException)
    {
      // SWALLOW -> UI handles it immediately, so we do not need to handle anything for now!
      // Idea for later -> when cancel called, create promise from UI to solve it later with this catch block.
      // So have 3 state on UI -> Cancellation clicked -> Cancelling -> Cancelled
      return;
    }
  }

  public void CancelSend(string modelCardId) => _cancellationManager.CancelOperation(modelCardId);

  /// <summary>
  /// Checks if any sender model cards contain any of the changed objects. If so, also updates the changed objects hashset for each model card - this last part is important for on send change detection.
  /// </summary>
  private void RunExpirationChecks()
  {
    var senders = _store.GetSenders();
    string[] objectIdsList = ChangedObjectIds.ToArray();
    List<string> expiredSenderIds = new();

    _sendConversionCache.EvictObjects(objectIdsList);

    foreach (SenderModelCard modelCard in senders)
    {
      var intersection = modelCard.SendFilter.NotNull().GetObjectIds().Intersect(objectIdsList).ToList();
      var isExpired = intersection.Count != 0;
      if (isExpired)
      {
        expiredSenderIds.Add(modelCard.ModelCardId.NotNull());
      }
    }

    Commands.SetModelsExpired(expiredSenderIds);
    ChangedObjectIds = new HashSet<string>();
  }
}
