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
using Speckle.Connectors.Rhino.Operations.Send.Filters;
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
  private readonly IServiceProvider _serviceProvider;
  private readonly ICancellationManager _cancellationManager;
  private readonly ISendConversionCache _sendConversionCache;
  private readonly IOperationProgressManager _operationProgressManager;
  private readonly ILogger<RhinoSendBinding> _logger;
  private readonly IRhinoConversionSettingsFactory _rhinoConversionSettingsFactory;
  private readonly ISpeckleApplication _speckleApplication;
  private readonly ISdkActivityFactory _activityFactory;
  private readonly ITopLevelExceptionHandler _topLevelExceptionHandler;
  private readonly IAppIdleManager _idleManager;

  /// <summary>
  /// Used internally to aggregate the changed objects' id. Objects in this list will be reconverted.
  ///
  /// Note we're using a concurrent dictionary here as the expiry check method is not thread safe, and this was causing problems. See:
  /// [CNX-202: Unhandled Exception Occurred when receiving in Rhino](https://linear.app/speckle/issue/CNX-202/unhandled-exception-occurred-when-receiving-in-rhino)
  /// As to why a concurrent dictionary, it's because it's the cheapest/easiest way to do so.
  /// https://stackoverflow.com/questions/18922985/concurrent-hashsett-in-net-framework
  /// </summary>
  private ConcurrentDictionary<string, byte> ChangedObjectIds { get; set; } = new();

  /// <summary>
  /// Stores objects that have "changed" only the commit structure/proxies - they do not need to be reconverted.
  /// </summary>
  private ConcurrentDictionary<string, byte> ChangedObjectIdsInGroupsOrLayers { get; set; } = new();
  private ConcurrentDictionary<int, byte> ChangedMaterialIndexes { get; set; } = new();

  private UnitSystem PreviousUnitSystem { get; set; }

  public RhinoSendBinding(
    DocumentModelStore store,
    IAppIdleManager idleManager,
    IBrowserBridge parent,
    IServiceProvider serviceProvider,
    ICancellationManager cancellationManager,
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
    _cancellationManager = cancellationManager;
    _sendConversionCache = sendConversionCache;
    _operationProgressManager = operationProgressManager;
    _logger = logger;
    _rhinoConversionSettingsFactory = rhinoConversionSettingsFactory;
    _speckleApplication = speckleApplication;
    Parent = parent;
    _topLevelExceptionHandler = topLevelExceptionHandler;
    Commands = new SendBindingUICommands(parent); // POC: Commands are tightly coupled with their bindings, at least for now, saves us injecting a factory.
    _activityFactory = activityFactory;
    PreviousUnitSystem = RhinoDoc.ActiveDoc.ModelUnitSystem;
    SubscribeToRhinoEvents();
  }

#pragma warning disable CA1502
  private void SubscribeToRhinoEvents()
#pragma warning restore CA1502
  {
    Command.BeginCommand += (_, e) =>
    {
      if (e.CommandEnglishName == "BlockEdit")
      {
        var selectedObject = RhinoDoc.ActiveDoc.Objects.GetSelectedObjects(false, false).First();
        ChangedObjectIds[selectedObject.Id.ToString()] = 1;
      }

      if (e.CommandEnglishName == "Ungroup")
      {
        foreach (RhinoObject selectedObject in RhinoDoc.ActiveDoc.Objects.GetSelectedObjects(false, false))
        {
          ChangedObjectIdsInGroupsOrLayers[selectedObject.Id.ToString()] = 1;
        }
        _idleManager.SubscribeToIdle(nameof(RunExpirationChecks), RunExpirationChecks);
      }
    };

    RhinoDoc.ActiveDocumentChanged += (_, e) =>
    {
      PreviousUnitSystem = e.Document.ModelUnitSystem;
    };

    // NOTE: BE CAREFUL handling things in this event handler since it is triggered whenever we save something into file!
    RhinoDoc.DocumentPropertiesChanged += async (_, e) =>
      await _topLevelExceptionHandler.CatchUnhandledAsync(async () =>
      {
        var newUnit = e.Document.ModelUnitSystem;
        if (newUnit != PreviousUnitSystem)
        {
          PreviousUnitSystem = newUnit;

          await InvalidateAllSender();
        }
      });

    RhinoDoc.AddRhinoObject += (_, e) =>
      _topLevelExceptionHandler.CatchUnhandled(() =>
      {
        if (!_store.IsDocumentInit)
        {
          return;
        }

        ChangedObjectIds[e.ObjectId.ToString()] = 1;
        _idleManager.SubscribeToIdle(nameof(RunExpirationChecks), RunExpirationChecks);
      });

    RhinoDoc.DeleteRhinoObject += (_, e) =>
      _topLevelExceptionHandler.CatchUnhandled(() =>
      {
        if (!_store.IsDocumentInit)
        {
          return;
        }

        ChangedObjectIds[e.ObjectId.ToString()] = 1;
        _idleManager.SubscribeToIdle(nameof(RunExpirationChecks), RunExpirationChecks);
      });

    // NOTE: Catches an object's material change from one user defined doc material to another. Does not catch (as the top event is not triggered) swapping material sources for an object or moving to/from the default material (this is handled below)!
    RhinoDoc.RenderMaterialsTableEvent += (_, args) =>
      _topLevelExceptionHandler.CatchUnhandled(() =>
      {
        if (!_store.IsDocumentInit)
        {
          return;
        }

        if (args is RhinoDoc.RenderMaterialAssignmentChangedEventArgs changedEventArgs)
        {
          // Update ChangedObjectIdsInGroupsOrLayers (without triggering objects' expiration)
          // 1. If Material was changed directly on the object:
          if (changedEventArgs.ObjectId != Guid.Empty)
          {
            ChangedObjectIdsInGroupsOrLayers[changedEventArgs.ObjectId.ToString()] = 1;
          }
          // 2. If parent Layer material has changed:
          else if (changedEventArgs.LayerId != Guid.Empty)
          {
            var layer = RhinoDoc.ActiveDoc.Layers.FindId(changedEventArgs.LayerId);
            foreach (Guid objectId in GetChildObjectIdsFromLayerAndSubLayers(layer))
            {
              ChangedObjectIdsInGroupsOrLayers[objectId.ToString()] = 1;
            }
          }
          _idleManager.SubscribeToIdle(nameof(RunExpirationChecks), RunExpirationChecks);
        }
      });

    RhinoDoc.GroupTableEvent += (_, args) =>
      _topLevelExceptionHandler.CatchUnhandled(() =>
      {
        if (!_store.IsDocumentInit)
        {
          return;
        }

        foreach (var obj in RhinoDoc.ActiveDoc.Groups.GroupMembers(args.GroupIndex))
        {
          ChangedObjectIdsInGroupsOrLayers[obj.Id.ToString()] = 1;
        }
        _idleManager.SubscribeToIdle(nameof(RunExpirationChecks), RunExpirationChecks);
      });

    RhinoDoc.LayerTableEvent += (_, args) =>
      _topLevelExceptionHandler.CatchUnhandled(async () =>
      {
        if (!_store.IsDocumentInit)
        {
          return;
        }

        if (
          args.EventType == LayerTableEventType.Deleted
          || args.EventType == LayerTableEventType.Current
          || args.EventType == LayerTableEventType.Added
        )
        {
          return;
        }

        var layer = RhinoDoc.ActiveDoc.Layers[args.LayerIndex];
        // Record IDs of all sub-objects affected by the LayerTable event (without triggering each objects' expiration)
        foreach (Guid objectId in GetChildObjectIdsFromLayerAndSubLayers(layer))
        {
          ChangedObjectIdsInGroupsOrLayers[objectId.ToString()] = 1;
        }
        _idleManager.SubscribeToIdle(nameof(RunExpirationChecks), RunExpirationChecks);
        await Commands.RefreshSendFilters();
      });

    // Catches and stores changed material ids. These are then used in the expiry checks to invalidate all objects that have assigned any of those material ids.
    RhinoDoc.MaterialTableEvent += (_, args) =>
      _topLevelExceptionHandler.CatchUnhandled(() =>
      {
        if (!_store.IsDocumentInit)
        {
          return;
        }

        if (args.EventType == MaterialTableEventType.Modified)
        {
          ChangedMaterialIndexes[args.Index] = 1;
          _idleManager.SubscribeToIdle(nameof(RunExpirationChecks), RunExpirationChecks);
        }
      });

    RhinoDoc.ModifyObjectAttributes += (_, e) =>
      _topLevelExceptionHandler.CatchUnhandled(() =>
      {
        if (!_store.IsDocumentInit)
        {
          return;
        }

        // NOTE: not sure yet we want to track every attribute changes yet. Explicitly tracking atts that change commit data. TBD
        if (
          e.OldAttributes.LayerIndex != e.NewAttributes.LayerIndex
          || e.OldAttributes.MaterialSource != e.NewAttributes.MaterialSource
          || e.OldAttributes.MaterialIndex != e.NewAttributes.MaterialIndex // NOTE: this does not work when swapping around from custom doc materials, it works when you swap TO/FROM default material
          || e.OldAttributes.ColorSource != e.NewAttributes.ColorSource
          || e.OldAttributes.ObjectColor != e.NewAttributes.ObjectColor
          || e.OldAttributes.Name != e.NewAttributes.Name
          || e.OldAttributes.UserStringCount != e.NewAttributes.UserStringCount
          || e.OldAttributes.GetUserStrings() != e.NewAttributes.GetUserStrings()
        )
        {
          ChangedObjectIds[e.RhinoObject.Id.ToString()] = 1;
          _idleManager.SubscribeToIdle(nameof(RunExpirationChecks), RunExpirationChecks);
        }
      });

    RhinoDoc.ReplaceRhinoObject += (_, e) =>
      _topLevelExceptionHandler.CatchUnhandled(() =>
      {
        if (!_store.IsDocumentInit)
        {
          return;
        }

        ChangedObjectIds[e.NewRhinoObject.Id.ToString()] = 1;
        ChangedObjectIds[e.OldRhinoObject.Id.ToString()] = 1;
        _idleManager.SubscribeToIdle(nameof(RunExpirationChecks), RunExpirationChecks);
      });
  }

  public List<ISendFilter> GetSendFilters() =>
    [new RhinoSelectionFilter() { IsDefault = true }, new RhinoLayersFilter()];

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

      using var cancellationItem = _cancellationManager.GetCancellationItem(modelCardId);

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
          modelCard.GetSendInfo(_speckleApplication.ApplicationAndVersion),
          _operationProgressManager.CreateOperationProgressEventHandler(Parent, modelCardId, cancellationItem.Token),
          cancellationItem.Token
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

    if (ChangedObjectIds.IsEmpty && ChangedObjectIdsInGroupsOrLayers.IsEmpty)
    {
      return;
    }

    // Actual model card invalidation
    string[] objectIdsList = ChangedObjectIds.Keys.ToArray();
    var changedObjectIdsInGroupsOrLayers = ChangedObjectIdsInGroupsOrLayers.Keys.ToArray();
    _sendConversionCache.EvictObjects(objectIdsList);
    var senders = _store.GetSenders();
    List<string> expiredSenderIds = new();

    foreach (SenderModelCard modelCard in senders)
    {
      var intersection = modelCard.SendFilter.NotNull().SelectedObjectIds.Intersect(objectIdsList);
      if (intersection.Any())
      {
        expiredSenderIds.Add(modelCard.ModelCardId.NotNull());
        continue;
      }

      var groupOrLayerIntersection = modelCard
        .SendFilter.NotNull()
        .SelectedObjectIds.Intersect(changedObjectIdsInGroupsOrLayers);
      if (groupOrLayerIntersection.Any())
      {
        expiredSenderIds.Add(modelCard.ModelCardId.NotNull());
        continue;
      }
    }

    await Commands.SetModelsExpired(expiredSenderIds);
    ChangedObjectIds = new();
    ChangedObjectIdsInGroupsOrLayers = new();
    ChangedMaterialIndexes = new();
  }

  private async Task InvalidateAllSender()
  {
    _sendConversionCache.ClearCache();
    var senderModelCardIds = _store.GetSenders().Select(s => s.ModelCardId.NotNull());
    await Commands.SetModelsExpired(senderModelCardIds);
  }

  private IEnumerable<Guid> GetChildObjectIdsFromLayerAndSubLayers(Layer layer)
  {
    var allLayers = RhinoDoc.ActiveDoc.Layers.Where(l => /* NOTE: layer path may actually be null in some cases (rhino's fault, not ours) */
      l.FullPath != null && l.FullPath.Contains(layer.Name)
    ); // not  e imperfect, but layer.GetChildren(true) is valid only in v8 and above; this filter will include the original layer.

    foreach (var childLayer in allLayers)
    {
      var sublayerObjs = RhinoDoc.ActiveDoc.Objects.FindByLayer(childLayer) ?? [];
      foreach (var obj in sublayerObjs)
      {
        yield return obj.Id;
      }
    }
  }
}
