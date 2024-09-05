using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Speckle.Autofac.DependencyInjection;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Exceptions;
using Speckle.Connectors.DUI.Logging;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.Settings;
using Speckle.Connectors.Rhino.HostApp;
using Speckle.Connectors.Utils.Caching;
using Speckle.Connectors.Utils.Cancellation;
using Speckle.Connectors.Utils.Operations;
using Speckle.Converters.Common;
using Speckle.Converters.Rhino;
using Speckle.Sdk;
using Speckle.Sdk.Common;

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
  private readonly ISendConversionCache _sendConversionCache;
  private readonly IOperationProgressManager _operationProgressManager;
  private readonly ILogger<RhinoSendBinding> _logger;
  private readonly ITopLevelExceptionHandler _topLevelExceptionHandler;
  private readonly IRhinoConversionSettingsFactory _rhinoConversionSettingsFactory;

  /// <summary>
  /// Used internally to aggregate the changed objects' id. Note we're using a concurrent dictionary here as the expiry check method is not thread safe, and this was causing problems. See:
  /// [CNX-202: Unhandled Exception Occurred when receiving in Rhino](https://linear.app/speckle/issue/CNX-202/unhandled-exception-occurred-when-receiving-in-rhino)
  /// As to why a concurrent dictionary, it's because it's the cheapest/easiest way to do so.
  /// https://stackoverflow.com/questions/18922985/concurrent-hashsett-in-net-framework
  /// </summary>
  private ConcurrentDictionary<string, byte> ChangedObjectIds { get; set; } = new();

  public RhinoSendBinding(
    DocumentModelStore store,
    IRhinoIdleManager idleManager,
    IBridge parent,
    IEnumerable<ISendFilter> sendFilters,
    IUnitOfWorkFactory unitOfWorkFactory,
    CancellationManager cancellationManager,
    ISendConversionCache sendConversionCache,
    IOperationProgressManager operationProgressManager,
    ILogger<RhinoSendBinding> logger,
    IRhinoConversionSettingsFactory rhinoConversionSettingsFactory
  )
  {
    _store = store;
    _idleManager = idleManager;
    _unitOfWorkFactory = unitOfWorkFactory;
    _sendFilters = sendFilters.ToList();
    _cancellationManager = cancellationManager;
    _sendConversionCache = sendConversionCache;
    _operationProgressManager = operationProgressManager;
    _logger = logger;
    _rhinoConversionSettingsFactory = rhinoConversionSettingsFactory;
    _topLevelExceptionHandler = parent.TopLevelExceptionHandler.Parent.TopLevelExceptionHandler;
    Parent = parent;
    Commands = new SendBindingUICommands(parent); // POC: Commands are tightly coupled with their bindings, at least for now, saves us injecting a factory.
    SubscribeToRhinoEvents();
  }

  private void SubscribeToRhinoEvents()
  {
    Command.BeginCommand += (_, e) =>
    {
      if (e.CommandEnglishName == "BlockEdit")
      {
        var selectedObject = RhinoDoc.ActiveDoc.Objects.GetSelectedObjects(false, false).First();
        ChangedObjectIds[selectedObject.Id.ToString()] = 1;
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

        ChangedObjectIds[e.ObjectId.ToString()] = 1;
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

        ChangedObjectIds[e.ObjectId.ToString()] = 1;
        _idleManager.SubscribeToIdle(nameof(RhinoSendBinding), RunExpirationChecks);
      });

    RhinoDoc.ModifyObjectAttributes += (_, e) =>
      _topLevelExceptionHandler.CatchUnhandled(() =>
      {
        // NOTE: This does not work if rhino starts and opens a blank doc;
        if (!_store.IsDocumentInit)
        {
          return;
        }

        // NOTE: not sure yet we want to track every attribute changes yet. TBD
        if (e.OldAttributes.LayerIndex != e.NewAttributes.LayerIndex)
        {
          ChangedObjectIds[e.RhinoObject.Id.ToString()] = 1;
          _idleManager.SubscribeToIdle(nameof(RhinoSendBinding), RunExpirationChecks);
        }
      });

    RhinoDoc.ReplaceRhinoObject += (_, e) =>
      _topLevelExceptionHandler.CatchUnhandled(() =>
      {
        // NOTE: This does not work if rhino starts and opens a blank doc;
        if (!_store.IsDocumentInit)
        {
          return;
        }

        ChangedObjectIds[e.NewRhinoObject.Id.ToString()] = 1;
        ChangedObjectIds[e.OldRhinoObject.Id.ToString()] = 1;
        _idleManager.SubscribeToIdle(nameof(RhinoSendBinding), RunExpirationChecks);
      });
  }

  public List<ISendFilter> GetSendFilters() => _sendFilters;

  public List<ICardSetting> GetSendSettings() => [];

  public async Task Send(string modelCardId)
  {
    using var unitOfWork = _unitOfWorkFactory.Create();
    using var settings = unitOfWork
      .Resolve<IConverterSettingsStore<RhinoConversionSettings>>()
      .Push(_ => _rhinoConversionSettingsFactory.Create(RhinoDoc.ActiveDoc));
    try
    {
      if (_store.GetModelById(modelCardId) is not SenderModelCard modelCard)
      {
        // Handle as GLOBAL ERROR at BrowserBridge
        throw new InvalidOperationException("No publish model card was found.");
      }

      CancellationToken cancellationToken = _cancellationManager.InitCancellationTokenSource(modelCardId);

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
        .Resolve<SendOperation<RhinoObject>>()
        .Execute(
          rhinoObjects,
          modelCard.GetSendInfo(Speckle.Connectors.Utils.Connector.Slug),
          (status, progress) =>
            _operationProgressManager.SetModelProgress(
              Parent,
              modelCardId,
              new ModelCardProgress(modelCardId, status, progress),
              cancellationToken
            ),
          cancellationToken
        )
        .ConfigureAwait(false);

      Commands.SetModelSendResult(modelCardId, sendResult.RootObjId, sendResult.ConversionResults);
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
      Commands.SetModelError(modelCardId, ex);
    }
  }

  public void CancelSend(string modelCardId) => _cancellationManager.CancelOperation(modelCardId);

  /// <summary>
  /// Checks if any sender model cards contain any of the changed objects. If so, also updates the changed objects hashset for each model card - this last part is important for on send change detection.
  /// </summary>
  private void RunExpirationChecks()
  {
    var senders = _store.GetSenders();
    string[] objectIdsList = ChangedObjectIds.Keys.ToArray(); // NOTE: could not copy to array happens here
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
    ChangedObjectIds = new();
  }
}
