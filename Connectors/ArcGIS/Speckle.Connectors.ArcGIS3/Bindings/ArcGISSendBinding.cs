using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using ArcGIS.Core.Data;
using ArcGIS.Desktop.Editing.Events;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using ArcGIS.Desktop.Mapping.Events;
using Microsoft.Extensions.Logging;
using Speckle.Autofac.DependencyInjection;
using Speckle.Connectors.ArcGIS.Filters;
using Speckle.Connectors.ArcGIS.Utils;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Exceptions;
using Speckle.Connectors.DUI.Logging;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.Settings;
using Speckle.Connectors.Utils.Caching;
using Speckle.Connectors.Utils.Cancellation;
using Speckle.Connectors.Utils.Operations;
using Speckle.Sdk;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.ArcGIS.Bindings;

public sealed class ArcGISSendBinding : ISendBinding
{
  public string Name => "sendBinding";
  public SendBindingUICommands Commands { get; }
  public IBridge Parent { get; }

  private readonly DocumentModelStore _store;
  private readonly IUnitOfWorkFactory _unitOfWorkFactory; // POC: unused? :D
  private readonly List<ISendFilter> _sendFilters;
  private readonly CancellationManager _cancellationManager;
  private readonly ISendConversionCache _sendConversionCache;
  private readonly IOperationProgressManager _operationProgressManager;
  private readonly ILogger<ArcGISSendBinding> _logger;
  private readonly ITopLevelExceptionHandler _topLevelExceptionHandler;
  private readonly MapMembersUtils _mapMemberUtils;

  /// <summary>
  /// Used internally to aggregate the changed objects' id. Note we're using a concurrent dictionary here as the expiry check method is not thread safe, and this was causing problems. See:
  /// [CNX-202: Unhandled Exception Occurred when receiving in Rhino](https://linear.app/speckle/issue/CNX-202/unhandled-exception-occurred-when-receiving-in-rhino)
  /// As to why a concurrent dictionary, it's because it's the cheapest/easiest way to do so.
  /// https://stackoverflow.com/questions/18922985/concurrent-hashsett-in-net-framework
  /// </summary>
  private ConcurrentDictionary<string, byte> ChangedObjectIds { get; set; } = new();

  private List<FeatureLayer> SubscribedLayers { get; set; } = new();
  private List<StandaloneTable> SubscribedTables { get; set; } = new();

  public ArcGISSendBinding(
    DocumentModelStore store,
    IBridge parent,
    IEnumerable<ISendFilter> sendFilters,
    IUnitOfWorkFactory unitOfWorkFactory,
    CancellationManager cancellationManager,
    ISendConversionCache sendConversionCache,
    IOperationProgressManager operationProgressManager,
    ILogger<ArcGISSendBinding> logger,
    MapMembersUtils mapMemberUtils
  )
  {
    _store = store;
    _unitOfWorkFactory = unitOfWorkFactory;
    _sendFilters = sendFilters.ToList();
    _cancellationManager = cancellationManager;
    _sendConversionCache = sendConversionCache;
    _operationProgressManager = operationProgressManager;
    _logger = logger;
    _topLevelExceptionHandler = parent.TopLevelExceptionHandler;
    _mapMemberUtils = mapMemberUtils;

    Parent = parent;
    Commands = new SendBindingUICommands(parent);
    SubscribeToArcGISEvents();
  }

  private void SubscribeToArcGISEvents()
  {
    LayersRemovedEvent.Subscribe(
      a => _topLevelExceptionHandler.CatchUnhandled(() => GetIdsForLayersRemovedEvent(a)),
      true
    );

    StandaloneTablesRemovedEvent.Subscribe(
      a => _topLevelExceptionHandler.CatchUnhandled(() => GetIdsForStandaloneTablesRemovedEvent(a)),
      true
    );

    MapPropertyChangedEvent.Subscribe(
      a => _topLevelExceptionHandler.CatchUnhandled(() => GetIdsForMapPropertyChangedEvent(a)),
      true
    ); // Map units, CRS etc.

    MapMemberPropertiesChangedEvent.Subscribe(
      a => _topLevelExceptionHandler.CatchUnhandled(() => GetIdsForMapMemberPropertiesChangedEvent(a)),
      true
    ); // e.g. Layer name

    ActiveMapViewChangedEvent.Subscribe(
      _ => _topLevelExceptionHandler.CatchUnhandled(SubscribeToMapMembersDataSourceChange),
      true
    );

    /*
    LayersAddedEvent.Subscribe(a => _topLevelExceptionHandler.CatchUnhandled(() => GetIdsForLayersAddedEvent(a)), true);

    StandaloneTablesAddedEvent.Subscribe(
      a => _topLevelExceptionHandler.CatchUnhandled(() => GetIdsForStandaloneTablesAddedEvent(a)),
      true
    );
    */
  }

  private void SubscribeToMapMembersDataSourceChange()
  {
    var task = QueuedTask.Run(() =>
    {
      if (MapView.Active == null)
      {
        return;
      }

      // subscribe to layers
      foreach (Layer layer in MapView.Active.Map.Layers)
      {
        if (layer is FeatureLayer featureLayer)
        {
          SubscribeToFeatureLayerDataSourceChange(featureLayer);
        }
      }
      // subscribe to tables
      foreach (StandaloneTable table in MapView.Active.Map.StandaloneTables)
      {
        SubscribeToTableDataSourceChange(table);
      }
    });
    task.Wait();
  }

  private void SubscribeToFeatureLayerDataSourceChange(FeatureLayer layer)
  {
    if (SubscribedLayers.Contains(layer))
    {
      return;
    }
    Table layerTable = layer.GetTable();
    if (layerTable != null)
    {
      SubscribeToAnyDataSourceChange(layerTable);
      SubscribedLayers.Add(layer);
    }
  }

  private void SubscribeToTableDataSourceChange(StandaloneTable table)
  {
    if (SubscribedTables.Contains(table))
    {
      return;
    }
    Table layerTable = table.GetTable();
    if (layerTable != null)
    {
      SubscribeToAnyDataSourceChange(layerTable);
      SubscribedTables.Add(table);
    }
  }

  private void SubscribeToAnyDataSourceChange(Table layerTable)
  {
    RowCreatedEvent.Subscribe(
      (args) =>
      {
        OnRowChanged(args);
      },
      layerTable
    );
    RowChangedEvent.Subscribe(
      (args) =>
      {
        OnRowChanged(args);
      },
      layerTable
    );
    RowDeletedEvent.Subscribe(
      (args) =>
      {
        OnRowChanged(args);
      },
      layerTable
    );
  }

  private void OnRowChanged(RowChangedEventArgs args)
  {
    if (args == null || MapView.Active == null)
    {
      return;
    }

    // get the path of the edited dataset
    Uri datasetPath = args.Row.GetTable().GetPath();

    // find all layers & tables reading from the dataset
    List<MapMember> allMapMembers = _mapMemberUtils.GetAllMapMembers(MapView.Active.Map);
    foreach (MapMember mapMember in allMapMembers)
    {
      try
      {
        if (mapMember is Layer layer)
        {
          if (layer.GetPath() == datasetPath)
          {
            ChangedObjectIds[layer.URI] = 1;
          }
        }

        if (mapMember is StandaloneTable table)
        {
          if (table.GetPath() == datasetPath)
          {
            ChangedObjectIds[table.URI] = 1;
          }
        }
      }
      catch (UriFormatException) // layer.GetPath() or table.GetPath() can throw this error, if data source was removed from the hard drive
      {
        // ignore layers with invalid source URI
      }
    }
    RunExpirationChecks(false);
  }

  private void GetIdsForLayersRemovedEvent(LayerEventsArgs args)
  {
    foreach (Layer layer in args.Layers)
    {
      ChangedObjectIds[layer.URI] = 1;
    }
    RunExpirationChecks(true);
  }

  private void GetIdsForStandaloneTablesRemovedEvent(StandaloneTableEventArgs args)
  {
    foreach (StandaloneTable table in args.Tables)
    {
      ChangedObjectIds[table.URI] = 1;
    }
    RunExpirationChecks(true);
  }

  private void AddChangedNestedObjectIds(GroupLayer group)
  {
    ChangedObjectIds[group.URI] = 1;
    foreach (var member in group.Layers)
    {
      if (member is GroupLayer subGroup)
      {
        AddChangedNestedObjectIds(subGroup);
      }
      else
      {
        ChangedObjectIds[member.URI] = 1;
      }
    }
  }

  private void GetIdsForMapPropertyChangedEvent(MapPropertyChangedEventArgs args)
  {
    foreach (Map map in args.Maps)
    {
      foreach (MapMember member in map.Layers)
      {
        if (member is GroupLayer group)
        {
          AddChangedNestedObjectIds(group);
        }
        else
        {
          ChangedObjectIds[member.URI] = 1;
        }
      }
    }
    RunExpirationChecks(false);
  }

  private void GetIdsForLayersAddedEvent(LayerEventsArgs args)
  {
    foreach (Layer layer in args.Layers)
    {
      if (layer is FeatureLayer featureLayer)
      {
        SubscribeToFeatureLayerDataSourceChange(featureLayer);
      }
    }
  }

  private void GetIdsForStandaloneTablesAddedEvent(StandaloneTableEventArgs args)
  {
    foreach (StandaloneTable table in args.Tables)
    {
      SubscribeToTableDataSourceChange(table);
    }
  }

  private void GetIdsForMapMemberPropertiesChangedEvent(MapMemberPropertiesChangedEventArgs args)
  {
    // don't subscribe to all events (e.g. expanding group, changing visibility etc.)
    bool validEvent = false;
    foreach (var hint in args.EventHints)
    {
      if (
        hint == MapMemberEventHint.DataSource
        || hint == MapMemberEventHint.DefinitionQuery
        || hint == MapMemberEventHint.LabelClasses
        || hint == MapMemberEventHint.LabelVisibility
        || hint == MapMemberEventHint.Name
        || hint == MapMemberEventHint.Renderer
        || hint == MapMemberEventHint.SceneLayerType
        || hint == MapMemberEventHint.URL
      )
      {
        validEvent = true;
        break;
      }
    }

    if (validEvent)
    {
      foreach (MapMember member in args.MapMembers)
      {
        ChangedObjectIds[member.URI] = 1;
      }
      RunExpirationChecks(false);
    }
  }

  public List<ISendFilter> GetSendFilters() => _sendFilters;

  public List<ICardSetting> GetSendSettings() => [];

  [SuppressMessage(
    "Maintainability",
    "CA1506:Avoid excessive class coupling",
    Justification = "Being refactored on in parallel, muting this issue so CI can pass initially."
  )]
  public async Task Send(string modelCardId)
  {
    //poc: dupe code between connectors
    using var unitOfWork = _unitOfWorkFactory.Resolve<SendOperation<MapMember>>();
    try
    {
      if (_store.GetModelById(modelCardId) is not SenderModelCard modelCard)
      {
        // Handle as GLOBAL ERROR at BrowserBridge
        throw new InvalidOperationException("No publish model card was found.");
      }

      CancellationToken cancellationToken = _cancellationManager.InitCancellationTokenSource(modelCardId);

      var sendResult = await QueuedTask
        .Run(async () =>
        {
          List<MapMember> mapMembers = modelCard
            .SendFilter.NotNull()
            .GetObjectIds()
            .Select(id => (MapMember)MapView.Active.Map.FindLayer(id) ?? MapView.Active.Map.FindStandaloneTable(id))
            .Where(obj => obj != null)
            .ToList();

          if (mapMembers.Count == 0)
          {
            // Handle as CARD ERROR in this function
            throw new SpeckleSendFilterException(
              "No objects were found to convert. Please update your publish filter!"
            );
          }

          // subscribe to the selected layer events
          foreach (MapMember mapMember in mapMembers)
          {
            if (mapMember is FeatureLayer featureLayer)
            {
              SubscribeToFeatureLayerDataSourceChange(featureLayer);
            }
            else if (mapMember is StandaloneTable table)
            {
              SubscribeToTableDataSourceChange(table);
            }
          }

          var result = await unitOfWork
            .Service.Execute(
              mapMembers,
              modelCard.GetSendInfo("ArcGIS"), // POC: get host app name from settings? same for GetReceiveInfo
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

          return result;
        })
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
  private void RunExpirationChecks(bool idsDeleted)
  {
    var senders = _store.GetSenders();
    List<string> expiredSenderIds = new();
    string[] objectIdsList = ChangedObjectIds.Keys.ToArray();

    _sendConversionCache.EvictObjects(objectIdsList);

    foreach (SenderModelCard sender in senders)
    {
      var objIds = sender.SendFilter.NotNull().GetObjectIds();
      var intersection = objIds.Intersect(objectIdsList).ToList();
      bool isExpired = sender.SendFilter.NotNull().CheckExpiry(objectIdsList);
      if (isExpired)
      {
        expiredSenderIds.Add(sender.ModelCardId.NotNull());

        // Update the model card object Ids
        if (idsDeleted && sender.SendFilter is ArcGISSelectionFilter filter)
        {
          List<string> remainingObjIds = objIds.SkipWhile(x => intersection.Contains(x)).ToList();
          filter.SelectedObjectIds = remainingObjIds;
        }
      }
    }

    Commands.SetModelsExpired(expiredSenderIds);
    ChangedObjectIds = new();
  }
}
