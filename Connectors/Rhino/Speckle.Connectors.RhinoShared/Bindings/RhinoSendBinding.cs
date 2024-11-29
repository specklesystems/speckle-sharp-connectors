using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.DocObjects.Tables;
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
using Speckle.Converters.Common;
using Speckle.Converters.Rhino;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Speckle.Sdk.Logging;

namespace Speckle.Connectors.Rhino.Bindings;

public sealed class RhinoSendBinding : ISendBinding
{
  public string Name => "sendBinding";
  public SendBindingUICommands Commands { get; }
  public IBrowserBridge Parent { get; }

  private readonly DocumentModelStore _store;
  private readonly IAppIdleManager _idleManager;
  private readonly IServiceProvider _serviceProvider;
  private readonly List<ISendFilter> _sendFilters;
  private readonly CancellationManager _cancellationManager;
  private readonly ISendConversionCache _sendConversionCache;
  private readonly IOperationProgressManager _operationProgressManager;
  private readonly ILogger<RhinoSendBinding> _logger;
  private readonly ITopLevelExceptionHandler _topLevelExceptionHandler;
  private readonly IRhinoConversionSettingsFactory _rhinoConversionSettingsFactory;
  private readonly ISpeckleApplication _speckleApplication;
  private readonly ISdkActivityFactory _activityFactory;

  /// <summary>
  /// Used internally to aggregate the changed objects' id. Note we're using a concurrent dictionary here as the expiry check method is not thread safe, and this was causing problems. See:
  /// [CNX-202: Unhandled Exception Occurred when receiving in Rhino](https://linear.app/speckle/issue/CNX-202/unhandled-exception-occurred-when-receiving-in-rhino)
  /// As to why a concurrent dictionary, it's because it's the cheapest/easiest way to do so.
  /// https://stackoverflow.com/questions/18922985/concurrent-hashsett-in-net-framework
  /// </summary>
  private ConcurrentDictionary<string, byte> ChangedObjectIds { get; set; } = new();
  private ConcurrentDictionary<int, byte> ChangedMaterialIndexes { get; set; } = new();

  private UnitSystem PreviousUnitSystem { get; set; }

  public RhinoSendBinding(
    DocumentModelStore store,
    IAppIdleManager idleManager,
    IBrowserBridge parent,
    IEnumerable<ISendFilter> sendFilters,
    IServiceProvider serviceProvider,
    CancellationManager cancellationManager,
    ISendConversionCache sendConversionCache,
    IOperationProgressManager operationProgressManager,
    ILogger<RhinoSendBinding> logger,
    IRhinoConversionSettingsFactory rhinoConversionSettingsFactory,
    ISpeckleApplication speckleApplication,
    ISdkActivityFactory activityFactory,
    ITopLevelExceptionHandler topLevelExceptionHandler
  )
  {
    _store = store;
    _idleManager = idleManager;
    _serviceProvider = serviceProvider;
    _sendFilters = sendFilters.ToList();
    _cancellationManager = cancellationManager;
    _sendConversionCache = sendConversionCache;
    _operationProgressManager = operationProgressManager;
    _logger = logger;
    _rhinoConversionSettingsFactory = rhinoConversionSettingsFactory;
    _speckleApplication = speckleApplication;
    _topLevelExceptionHandler = topLevelExceptionHandler;
    Parent = parent;
    Commands = new SendBindingUICommands(parent); // POC: Commands are tightly coupled with their bindings, at least for now, saves us injecting a factory.
    _activityFactory = activityFactory;
    PreviousUnitSystem = RhinoDoc.ActiveDoc.ModelUnitSystem;
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

    RhinoDoc.ActiveDocumentChanged += (_, e) =>
    {
      PreviousUnitSystem = e.Document.ModelUnitSystem;
    };

    // NOTE: BE CAREFUL handling things in this event handler since it is triggered whenever we save something into file!
    RhinoDoc.DocumentPropertiesChanged += async (_, e) =>
    {
      var newUnit = e.Document.ModelUnitSystem;
      if (newUnit != PreviousUnitSystem)
      {
        PreviousUnitSystem = newUnit;

        await InvalidateAllSender().ConfigureAwait(false);
      }
    };

    RhinoDoc.AddRhinoObject += (_, e) =>
      _topLevelExceptionHandler.CatchUnhandled(() =>
      {
        // NOTE: This does not work if rhino starts and opens a blank doc;
        // These events always happen in a doc. Why guard agains a null doc?
        // if (!_store.IsDocumentInit)
        // {
        //   return;
        // }

        ChangedObjectIds[e.ObjectId.ToString()] = 1;
        _idleManager.SubscribeToIdle(nameof(RhinoSendBinding), RunExpirationChecks);
      });

    RhinoDoc.DeleteRhinoObject += (_, e) =>
      _topLevelExceptionHandler.CatchUnhandled(() =>
      {
        // NOTE: This does not work if rhino starts and opens a blank doc;
        // These events always happen in a doc. Why guard agains a null doc?
        // if (!_store.IsDocumentInit)
        // {
        //   return;
        // }

        ChangedObjectIds[e.ObjectId.ToString()] = 1;
        _idleManager.SubscribeToIdle(nameof(RhinoSendBinding), RunExpirationChecks);
      });

    // NOTE: Catches an object's material change from one user defined doc material to another. Does not catch (as the top event is not triggered) swapping material sources for an object or moving to/from the default material (this is handled below)!
    RhinoDoc.RenderMaterialsTableEvent += (_, args) =>
      _topLevelExceptionHandler.CatchUnhandled(() =>
      {
        if (args is RhinoDoc.RenderMaterialAssignmentChangedEventArgs changedEventArgs)
        {
          ChangedObjectIds[changedEventArgs.ObjectId.ToString()] = 1;
          _idleManager.SubscribeToIdle(nameof(RhinoSendBinding), RunExpirationChecks);
        }
      });

    // Catches and stores changed material ids. These are then used in the expiry checks to invalidate all objects that have assigned any of those material ids.
    RhinoDoc.MaterialTableEvent += (_, args) =>
      _topLevelExceptionHandler.CatchUnhandled(() =>
      {
        if (args.EventType == MaterialTableEventType.Modified)
        {
          ChangedMaterialIndexes[args.Index] = 1;
          _idleManager.SubscribeToIdle(nameof(RhinoSendBinding), RunExpirationChecks);
        }
      });

    RhinoDoc.ModifyObjectAttributes += (_, e) =>
      _topLevelExceptionHandler.CatchUnhandled(() =>
      {
        // NOTE: This does not work if rhino starts and opens a blank doc;
        // These events always happen in a doc. Why guard agains a null doc?
        // if (!_store.IsDocumentInit)
        // {
        //   return;
        // }

        // NOTE: not sure yet we want to track every attribute changes yet. TBD
        // NOTE: we might want to track here user strings too (once we send them out), and more!
        if (
          e.OldAttributes.LayerIndex != e.NewAttributes.LayerIndex
          || e.OldAttributes.MaterialSource != e.NewAttributes.MaterialSource
          || e.OldAttributes.MaterialIndex != e.NewAttributes.MaterialIndex // NOTE: this does not work when swapping around from custom doc materials, it works when you swap TO/FROM default material
        )
        {
          ChangedObjectIds[e.RhinoObject.Id.ToString()] = 1;
          _idleManager.SubscribeToIdle(nameof(RhinoSendBinding), RunExpirationChecks);
        }
      });

    RhinoDoc.ReplaceRhinoObject += (_, e) =>
      _topLevelExceptionHandler.CatchUnhandled(() =>
      {
        // NOTE: This does not work if rhino starts and opens a blank doc;
        // These events always happen in a doc. Why guard agains a null doc?
        // if (!_store.IsDocumentInit)
        // {
        //   return;
        // }

        ChangedObjectIds[e.NewRhinoObject.Id.ToString()] = 1;
        ChangedObjectIds[e.OldRhinoObject.Id.ToString()] = 1;
        _idleManager.SubscribeToIdle(nameof(RhinoSendBinding), RunExpirationChecks);
      });
  }

  public List<ISendFilter> GetSendFilters() => _sendFilters;

  public List<ICardSetting> GetSendSettings() => [];

  public async Task Send(string modelCardId)
  {
    using var activity = _activityFactory.Start();
    using var scope = _serviceProvider.CreateScope();
    scope
      .ServiceProvider.GetRequiredService<IConverterSettingsStore<RhinoConversionSettings>>()
      .Initialize(_rhinoConversionSettingsFactory.Create(RhinoDoc.ActiveDoc));
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
        .RefreshObjectIds()
        .Select(id => RhinoDoc.ActiveDoc.Objects.FindId(new Guid(id)))
        .Where(obj => obj != null)
        .ToList();

      if (rhinoObjects.Count == 0)
      {
        // Handle as CARD ERROR in this function
        throw new SpeckleSendFilterException("No objects were found to convert. Please update your publish filter!");
      }

      var sendResult = await scope
        .ServiceProvider.GetRequiredService<SendOperation<RhinoObject>>()
        .Execute(
          rhinoObjects,
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
      return;
    }
    catch (Exception ex) when (!ex.IsFatal()) // UX reasons - we will report operation exceptions as model card error. We may change this later when we have more exception documentation
    {
      _logger.LogModelCardHandledError(ex);
      await Commands.SetModelError(modelCardId, ex).ConfigureAwait(false);
    }
  }

  public void CancelSend(string modelCardId) => _cancellationManager.CancelOperation(modelCardId);

  /// <summary>
  /// Checks if any sender model cards contain any of the changed objects. If so, also updates the changed objects hashset for each model card - this last part is important for on send change detection.
  /// </summary>
  private async Task RunExpirationChecks()
  {
    // Note: added here a guard against executing this if there's no active doc present.
    if (RhinoDoc.ActiveDoc == null)
    {
      _logger.LogError("Rhino expiration checks were running without an active doc.");
      return;
    }

    // Invalidate any objects whose materials have changed
    if (!ChangedMaterialIndexes.IsEmpty)
    {
      var changedMaterialIndexes = ChangedMaterialIndexes.Keys.ToArray();
      foreach (var rhinoObject in RhinoDoc.ActiveDoc.Objects)
      {
        if (changedMaterialIndexes.Contains(rhinoObject.Attributes.MaterialIndex))
        {
          ChangedObjectIds[rhinoObject.Id.ToString()] = 1;
        }
      }
    }

    if (ChangedObjectIds.IsEmpty)
    {
      return;
    }

    // Actual model card invalidation
    string[] objectIdsList = ChangedObjectIds.Keys.ToArray(); // NOTE: could not copy to array happens here
    _sendConversionCache.EvictObjects(objectIdsList);
    var senders = _store.GetSenders();
    List<string> expiredSenderIds = new();

    foreach (SenderModelCard modelCard in senders)
    {
      var intersection = modelCard.SendFilter.NotNull().SelectedObjectIds.Intersect(objectIdsList).ToList();
      var isExpired = intersection.Count != 0;
      if (isExpired)
      {
        expiredSenderIds.Add(modelCard.ModelCardId.NotNull());
      }
    }

    await Commands.SetModelsExpired(expiredSenderIds).ConfigureAwait(false);
    ChangedObjectIds = new();
    ChangedMaterialIndexes = new();
  }

  private async Task InvalidateAllSender()
  {
    _sendConversionCache.ClearCache();
    var senderModelCardIds = _store.GetSenders().Select(s => s.ModelCardId.NotNull());
    await Commands.SetModelsExpired(senderModelCardIds).ConfigureAwait(false);
  }
}
