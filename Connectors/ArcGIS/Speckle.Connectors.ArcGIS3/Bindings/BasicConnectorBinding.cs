using ArcGIS.Core.Data;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using Speckle.Connectors.ArcGIS.Utils;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using ArcProject = ArcGIS.Desktop.Core.Project;

namespace Speckle.Connectors.ArcGIS.Bindings;

//poc: dupe code between connectors
public class BasicConnectorBinding : IBasicConnectorBinding
{
  public string Name => "baseBinding";
  public IBrowserBridge Parent { get; }

  public BasicConnectorBindingCommands Commands { get; }
  private readonly DocumentModelStore _store;
  private readonly ISpeckleApplication _speckleApplication;

  public BasicConnectorBinding(DocumentModelStore store, IBrowserBridge parent, ISpeckleApplication speckleApplication)
  {
    _store = store;
    _speckleApplication = speckleApplication;
    Parent = parent;
    Commands = new BasicConnectorBindingCommands(parent);

    _store.DocumentChanged += (_, _) =>
      parent.TopLevelExceptionHandler.FireAndForget(async () =>
      {
        await Commands.NotifyDocumentChanged().ConfigureAwait(false);
      });
  }

  public string GetSourceApplicationName() => _speckleApplication.Slug;

  public string GetSourceApplicationVersion() => _speckleApplication.HostApplicationVersion;

  public string GetConnectorVersion() => _speckleApplication.SpeckleVersion;

  public DocumentInfo? GetDocumentInfo()
  {
    if (MapView.Active is null)
    {
      return null;
    }

    return new DocumentInfo(ArcProject.Current.URI, MapView.Active.Map.Name, MapView.Active.Map.URI);
  }

  public DocumentModelStore GetDocumentState() => _store;

  public void AddModel(ModelCard model) => _store.Models.Add(model);

  public void UpdateModel(ModelCard model) => _store.UpdateModel(model);

  public void RemoveModel(ModelCard model) => _store.RemoveModel(model);

  public async Task HighlightObjects(IReadOnlyList<string> objectIds) =>
    await HighlightObjectsOnView(objectIds.Select(x => new ObjectID(x)).ToList()).ConfigureAwait(false);

  public async Task HighlightModel(string modelCardId)
  {
    var model = _store.GetModelById(modelCardId);

    if (model is null)
    {
      return;
    }

    var objectIds = new List<ObjectID>();

    if (model is SenderModelCard senderModelCard)
    {
      objectIds = senderModelCard.SendFilter.NotNull().RefreshObjectIds().Select(x => new ObjectID(x)).ToList();
    }

    if (model is ReceiverModelCard receiverModelCard)
    {
      objectIds = receiverModelCard.BakedObjectIds.NotNull().Select(x => new ObjectID(x)).ToList();
    }

    if (objectIds is null)
    {
      return;
    }
    await HighlightObjectsOnView(objectIds).ConfigureAwait(false);
  }

  private async Task HighlightObjectsOnView(IReadOnlyList<ObjectID> objectIds)
  {
    MapView mapView = MapView.Active;

    await QueuedTask
      .Run(async () =>
      {
        List<MapMemberFeature> mapMembersFeatures = GetMapMembers(objectIds, mapView);
        ClearSelectionInTOC();
        ClearSelection();
        await SelectMapMembersInTOC(mapMembersFeatures).ConfigureAwait(false);
        SelectMapMembersAndFeatures(mapMembersFeatures);
        mapView.ZoomToSelected();
      })
      .ConfigureAwait(false);
  }

  private List<MapMemberFeature> GetMapMembers(IReadOnlyList<ObjectID> objectIds, MapView mapView)
  {
    // find the layer on the map (from the objectID) and add the featureID is available
    List<MapMemberFeature> mapMembersFeatures = new();

    foreach (ObjectID objectId in objectIds)
    {
      MapMember mapMember = mapView.Map.FindLayer(objectId.MappedLayerURI, true);
      if (mapMember is null)
      {
        mapMember = mapView.Map.FindStandaloneTable(objectId.MappedLayerURI);
      }
      if (mapMember is not null)
      {
        MapMemberFeature mapMembersFeat = new(mapMember, objectId.FeatureId);
        mapMembersFeatures.Add(mapMembersFeat);
      }
    }
    return mapMembersFeatures;
  }

  private void ClearSelection()
  {
    List<Layer> mapMembers = MapView.Active.Map.GetLayersAsFlattenedList().ToList();
    foreach (var member in mapMembers)
    {
      if (member is FeatureLayer featureLayer)
      {
        featureLayer.ClearSelection();
      }
    }
  }

  private void ClearSelectionInTOC()
  {
    MapView.Active.ClearTOCSelection();
  }

  private void SelectMapMembersAndFeatures(IReadOnlyList<MapMemberFeature> mapMembersFeatures)
  {
    foreach (MapMemberFeature mapMemberFeat in mapMembersFeatures)
    {
      MapMember member = mapMemberFeat.MapMember;
      if (member is FeatureLayer layer)
      {
        if (mapMemberFeat.FeatureId == null)
        {
          // select full layer if featureID not specified
          layer.Select();
        }
        else
        {
          // query features by ID
          var objectIDfield = layer.GetFeatureClass().GetDefinition().GetObjectIDField();

          // FeatureID range starts from 0, but auto-assigned IDs in the layer start from 1
          QueryFilter anotherQueryFilter = new() { WhereClause = $"{objectIDfield} = {mapMemberFeat.FeatureId + 1}" };
          using (Selection onlyOneSelection = layer.Select(anotherQueryFilter, SelectionCombinationMethod.New)) { }
        }
      }
    }
  }

  private async Task SelectMapMembersInTOC(IReadOnlyList<MapMemberFeature> mapMembersFeatures)
  {
    List<Layer> layers = new();
    List<StandaloneTable> tables = new();

    foreach (MapMemberFeature mapMemberFeat in mapMembersFeatures)
    {
      MapMember member = mapMemberFeat.MapMember;
      if (member is Layer layer)
      {
        if (member is not GroupLayer) // group layer selection clears other layers selection
        {
          layers.Add(layer);
        }
        else
        {
          await QueuedTask.Run(() => layer.SetExpanded(true)).ConfigureAwait(false);
        }
      }
      else if (member is StandaloneTable table)
      {
        tables.Add(table);
      }
    }
    MapView.Active.SelectLayers(layers);

    // this step clears previous selection, not clear how to ADD selection instead
    // this is why, activating it only if no layers are selected
    if (layers.Count == 0)
    {
      MapView.Active.SelectStandaloneTables(tables);
    }
  }
}
