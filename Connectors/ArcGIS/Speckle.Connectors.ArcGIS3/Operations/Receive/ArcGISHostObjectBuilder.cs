using System.Diagnostics.Contracts;
using ArcGIS.Core.CIM;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using Speckle.Connectors.ArcGIS.HostApp;
using Speckle.Connectors.ArcGIS.Utils;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Instances;
using Speckle.Connectors.Common.Operations;
using Speckle.Converters.ArcGIS3;
using Speckle.Converters.ArcGIS3.Utils;
using Speckle.Converters.Common;
using Speckle.Objects.GIS;
using Speckle.Objects.Other;
using Speckle.Sdk;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.GraphTraversal;
using Speckle.Sdk.Models.Instances;
using Speckle.Sdk.Models.Proxies;
using RasterLayer = Speckle.Objects.GIS.RasterLayer;

namespace Speckle.Connectors.ArcGIS.Operations.Receive;

public class ArcGISHostObjectBuilder : IHostObjectBuilder
{
  private readonly IRootToHostConverter _converter;
  private readonly IFeatureClassUtils _featureClassUtils;
  private readonly ILocalToGlobalUnpacker _localToGlobalUnpacker;
  private readonly LocalToGlobalConverterUtils _localToGlobalConverterUtils;
  private readonly ICrsUtils _crsUtils;

  // POC: figure out the correct scope to only initialize on Receive
  private readonly IConverterSettingsStore<ArcGISConversionSettings> _settingsStore;
  private readonly GraphTraversal _traverseFunction;
  private readonly ArcGISColorManager _colorManager;

  public ArcGISHostObjectBuilder(
    IRootToHostConverter converter,
    IConverterSettingsStore<ArcGISConversionSettings> settingsStore,
    IFeatureClassUtils featureClassUtils,
    ILocalToGlobalUnpacker localToGlobalUnpacker,
    LocalToGlobalConverterUtils localToGlobalConverterUtils,
    ICrsUtils crsUtils,
    GraphTraversal traverseFunction,
    ArcGISColorManager colorManager
  )
  {
    _converter = converter;
    _settingsStore = settingsStore;
    _featureClassUtils = featureClassUtils;
    _localToGlobalUnpacker = localToGlobalUnpacker;
    _localToGlobalConverterUtils = localToGlobalConverterUtils;
    _traverseFunction = traverseFunction;
    _colorManager = colorManager;
    _crsUtils = crsUtils;
  }

  public async Task<HostObjectBuilderResult> Build(
    Base rootObject,
    string projectName,
    string modelName,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    // TODO get spatialRef and offsets & rotation from ProjectInfo in CommitObject
    // ATM, GIS commit CRS is stored per layer (in FeatureClass converter), but should be moved to the Root level too

    // Prompt the UI conversion started. Progress bar will swoosh.
    onOperationProgressed.Report(new("Converting", null));

    // get materials
    List<RenderMaterialProxy>? materials = (rootObject[ProxyKeys.RENDER_MATERIAL] as List<object>)
      ?.Cast<RenderMaterialProxy>()
      .ToList();
    if (materials != null)
    {
      await _colorManager.ParseMaterials(materials, onOperationProgressed).ConfigureAwait(false);
    }

    // get colors
    List<ColorProxy>? colors = (rootObject[ProxyKeys.COLOR] as List<object>)?.Cast<ColorProxy>().ToList();
    if (colors != null)
    {
      await _colorManager.ParseColors(colors, onOperationProgressed).ConfigureAwait(false);
    }

    int count = 0;
    List<LocalToGlobalMap> objectsToConvert = GetObjectsToConvert(rootObject);
    Dictionary<TraversalContext, ObjectConversionTracker> conversionTracker = new();

    // 1. convert everything
    List<ReceiveConversionResult> results = new(objectsToConvert.Count);
    List<string> bakedObjectIds = new();
    foreach (LocalToGlobalMap objectToConvert in objectsToConvert)
    {
      string[] path = GetLayerPath(objectToConvert.TraversalContext);
      Base obj = objectToConvert.AtomicObject;

      cancellationToken.ThrowIfCancellationRequested();
      try
      {
        obj = _localToGlobalConverterUtils.TransformObjects(objectToConvert.AtomicObject, objectToConvert.Matrix);
        object? conversionResult =
          //(obj["displayValue"] is null || (obj["displayValue"] is IReadOnlyList<Base> list && list.Count == 0))
          //  ? null :
          await QueuedTask.Run(() => _converter.Convert(obj)).ConfigureAwait(false);

        string nestedLayerPath = $"{string.Join("\\", path)}";
        if (objectToConvert.TraversalContext.Parent?.Current is not VectorLayer)
        {
          nestedLayerPath += $"\\{obj.speckle_type.Split(".")[^1]}"; // add sub-layer by speckleType, for non-GIS objects
        }

        conversionTracker[objectToConvert.TraversalContext] = new ObjectConversionTracker(
          obj,
          (Geometry?)conversionResult,
          nestedLayerPath
        );
      }
      catch (Exception ex) when (!ex.IsFatal()) // DO NOT CATCH SPECIFIC STUFF, conversion errors should be recoverable
      {
        results.Add(new(Status.ERROR, obj, null, null, ex));
      }
      onOperationProgressed.Report(new("Converting", (double)++count / objectsToConvert.Count));
    }

    // 2.1. Group conversionTrackers (to write into datasets)
    onOperationProgressed.Report(new("Grouping features into layers", null));
    Dictionary<string, List<(TraversalContext, ObjectConversionTracker)>> convertedGroups = await QueuedTask
      .Run(async () =>
      {
        return await _featureClassUtils
          .GroupConversionTrackers(conversionTracker, (s, progres) => onOperationProgressed.Report(new(s, progres)))
          .ConfigureAwait(false);
      })
      .ConfigureAwait(false);

    // 2.2. Write groups of objects to Datasets
    onOperationProgressed.Report(new("Writing to Database", null));
    await QueuedTask
      .Run(async () =>
      {
        await _featureClassUtils
          .CreateDatasets(
            conversionTracker,
            convertedGroups,
            (s, progres) => onOperationProgressed.Report(new(s, progres))
          )
          .ConfigureAwait(false);
      })
      .ConfigureAwait(false);

    // 3. add layer and tables to the Map and Table Of Content

    // Create placeholder for GroupLayers
    Dictionary<string, GroupLayer> createdLayerGroups = new();

    int bakeCount = 0;
    Dictionary<string, (MapMember, CIMUniqueValueRenderer?)> bakedMapMembers = new();
    onOperationProgressed.Report(new("Adding to Map", bakeCount));

    foreach (var item in conversionTracker)
    {
      cancellationToken.ThrowIfCancellationRequested();
      var trackerItem = conversionTracker[item.Key]; // updated tracker object

      // BAKE OBJECTS HERE
      if (trackerItem.Exception != null)
      {
        results.Add(new(Status.ERROR, trackerItem.Base, null, null, trackerItem.Exception));
      }
      else if (trackerItem.DatasetId == null)
      {
        results.Add(
          new(
            Status.ERROR,
            trackerItem.Base,
            null,
            null,
            new ArgumentException($"Unknown error: Dataset not created for {trackerItem.Base.speckle_type}")
          )
        );
      }
      else if (bakedMapMembers.TryGetValue(trackerItem.DatasetId, out var value))
      {
        // if the layer already created, just add more features to report, and more color categories
        // add layer and layer URI to tracker
        trackerItem.AddConvertedMapMember(value.Item1);
        trackerItem.AddLayerURI(value.Item1.URI);
        conversionTracker[item.Key] = trackerItem; // not necessary atm, but needed if we use conversionTracker further

        // add color category
        CIMUniqueValueRenderer? uvr = _colorManager.CreateOrEditLayerRenderer(item.Key, trackerItem, value.Item2);
        // replace renderer
        bakedMapMembers[trackerItem.DatasetId] = (value.Item1, uvr);

        // only add a report item
        AddResultsFromTracker(trackerItem, results);
      }
      else
      {
        // no layer yet, create and add layer to Map
        MapMember mapMember = await AddDatasetsToMap(trackerItem, createdLayerGroups, projectName, modelName)
          .ConfigureAwait(false);

        // add layer and layer URI to tracker
        trackerItem.AddConvertedMapMember(mapMember);
        trackerItem.AddLayerURI(mapMember.URI);
        conversionTracker[item.Key] = trackerItem; // not necessary atm, but needed if we use conversionTracker further

        // add layer URI to bakedIds
        bakedObjectIds.Add(trackerItem.MappedLayerURI == null ? "" : trackerItem.MappedLayerURI);

        // add color category
        CIMUniqueValueRenderer? uvr = _colorManager.CreateOrEditLayerRenderer(item.Key, trackerItem, null);
        // mark dataset as already created
        bakedMapMembers[trackerItem.DatasetId] = (mapMember, uvr);

        // add report item
        AddResultsFromTracker(trackerItem, results);
      }

      onOperationProgressed.Report(new("Adding to Map", (double)++bakeCount / conversionTracker.Count));
    }

    // apply renderers to baked layers
    foreach (var bakedMember in bakedMapMembers)
    {
      if (bakedMember.Value.Item1 is FeatureLayer fLayer)
      {
        // Set the feature layer's renderer.
        await QueuedTask.Run(() => fLayer.SetRenderer(bakedMember.Value.Item2)).ConfigureAwait(false);
      }
    }
    bakedObjectIds.AddRange(createdLayerGroups.Values.Select(x => x.URI));

    // TODO: validated a correct set regarding bakedobject ids
    return new(bakedObjectIds, results);
  }

  private List<LocalToGlobalMap> GetObjectsToConvert(Base rootObject)
  {
    // keep GISlayers in the list, because they are still needed to extract CRS of the commit (code below)
    List<TraversalContext> objectsToConvertTc = _traverseFunction.Traverse(rootObject).ToList();

    // get CRS from any present VectorLayer
    Base? vLayer = objectsToConvertTc.FirstOrDefault(x => x.Current is VectorLayer)?.Current;
    using var crs = _crsUtils.FindSetCrsDataOnReceive(vLayer); // TODO help

    // now filter the objects
    objectsToConvertTc = objectsToConvertTc.Where(ctx => ctx.Current is not Collection).ToList();

    var instanceDefinitionProxies = (rootObject[ProxyKeys.INSTANCE_DEFINITION] as List<object>)
      ?.Cast<InstanceDefinitionProxy>()
      .ToList();

    return _localToGlobalUnpacker.Unpack(instanceDefinitionProxies, objectsToConvertTc);
  }

  private void AddResultsFromTracker(ObjectConversionTracker trackerItem, List<ReceiveConversionResult> results)
  {
    if (trackerItem.MappedLayerURI == null) // should not happen
    {
      results.Add(
        new(
          Status.ERROR,
          trackerItem.Base,
          null,
          null,
          new ArgumentException($"Created Layer URI not found for {trackerItem.Base.speckle_type}")
        )
      );
    }
    else
    {
      // encode layer ID and ID of its feature in 1 object represented as string
      ObjectID objectId = new(trackerItem.MappedLayerURI, trackerItem.DatasetRow);
      if (trackerItem.HostAppGeom != null) // individual hostAppGeometry
      {
        results.Add(
          new(
            Status.SUCCESS,
            trackerItem.Base,
            objectId.ObjectIdToString(),
            trackerItem.HostAppGeom.GetType().ToString()
          )
        );
      }
      else // hostApp Layers
      {
        results.Add(
          new(
            Status.SUCCESS,
            trackerItem.Base,
            objectId.ObjectIdToString(),
            trackerItem.HostAppMapMember?.GetType().ToString()
          )
        );
      }
    }
  }

  private async Task<MapMember> AddDatasetsToMap(
    ObjectConversionTracker trackerItem,
    Dictionary<string, GroupLayer> createdLayerGroups,
    string projectName,
    string modelName
  )
  {
    return await QueuedTask
      .Run(() =>
      {
        // get layer details
        string? datasetId = trackerItem.DatasetId; // should not be null here
        Uri uri = new($"{_settingsStore.Current.SpeckleDatabasePath.AbsolutePath.Replace('/', '\\')}\\{datasetId}");
        string nestedLayerName = trackerItem.NestedLayerName;

        // add group for the current layer
        string shortName = nestedLayerName.Split("\\")[^1];
        string nestedLayerPath = string.Join("\\", nestedLayerName.Split("\\").SkipLast(1));

        // if no general group layer found
        if (createdLayerGroups.Count == 0)
        {
          Map map = _settingsStore.Current.Map;
          GroupLayer mainGroupLayer = LayerFactory.Instance.CreateGroupLayer(map, 0, $"{projectName}: {modelName}");
          mainGroupLayer.SetExpanded(true);
          createdLayerGroups["Basic Speckle Group"] = mainGroupLayer; // key doesn't really matter here
        }

        var groupLayer = CreateNestedGroupLayer(nestedLayerPath, createdLayerGroups);

        // Most of the Speckle-written datasets will be containing geometry and added as Layers
        // although, some datasets might be just tables (e.g. native GIS Tables, in the future maybe Revit schedules etc.
        // We can create a connection to the dataset in advance and determine its type, but this will be more
        // expensive, than assuming by default that it's a layer with geometry (which in most cases it's expected to be)
        try
        {
          var layer = LayerFactory.Instance.CreateLayer(uri, groupLayer, layerName: shortName);
          if (layer == null)
          {
            throw new SpeckleException($"Layer '{shortName}' was not created");
          }
          layer.SetExpanded(false);

          // if Scene
          // https://community.esri.com/t5/arcgis-pro-sdk-questions/sdk-equivalent-to-changing-layer-s-elevation/td-p/1346139
          if (_settingsStore.Current.Map.IsScene)
          {
            var groundSurfaceLayer = _settingsStore.Current.Map.GetGroundElevationSurfaceLayer();
            var layerElevationSurface = new CIMLayerElevationSurface
            {
              ElevationSurfaceLayerURI = groundSurfaceLayer.URI,
            };

            // for Feature Layers
            if (layer.GetDefinition() is CIMFeatureLayer cimLyr)
            {
              cimLyr.LayerElevation = layerElevationSurface;
              layer.SetDefinition(cimLyr);
            }
          }

          return (MapMember)layer;
        }
        catch (ArgumentException)
        {
          StandaloneTable table = StandaloneTableFactory.Instance.CreateStandaloneTable(
            uri,
            groupLayer,
            tableName: shortName
          );
          return table;
        }
      })
      .ConfigureAwait(false);
  }

  private GroupLayer CreateNestedGroupLayer(string nestedLayerPath, Dictionary<string, GroupLayer> createdLayerGroups)
  {
    GroupLayer lastGroup = createdLayerGroups.FirstOrDefault().Value;
    if (lastGroup == null) // if layer not found
    {
      throw new InvalidOperationException("Speckle Layer Group not found");
    }

    // iterate through each nested level
    string createdGroupPath = "";
    var allPathElements = nestedLayerPath.Split("\\").Where(x => !string.IsNullOrEmpty(x));
    foreach (string pathElement in allPathElements)
    {
      createdGroupPath += "\\" + pathElement;
      if (createdLayerGroups.TryGetValue(createdGroupPath, out var existingGroupLayer))
      {
        lastGroup = existingGroupLayer;
      }
      else
      {
        // create new GroupLayer under last found Group, named with last pathElement
        lastGroup = LayerFactory.Instance.CreateGroupLayer(lastGroup, 0, pathElement);
        lastGroup.SetExpanded(true);
      }
      createdLayerGroups[createdGroupPath] = lastGroup;
    }
    return lastGroup;
  }

  [Pure]
  private static string[] GetLayerPath(TraversalContext context)
  {
    string[] collectionBasedPath = context.GetAscendantOfType<Collection>().Select(c => c.name).ToArray();
    string[] reverseOrderPath =
      collectionBasedPath.Length != 0 ? collectionBasedPath : context.GetPropertyPath().ToArray();

    var originalPath = reverseOrderPath.Reverse().ToArray();
    return originalPath.Where(x => !string.IsNullOrEmpty(x)).ToArray();
  }

  [Pure]
  private static bool HasGISParent(TraversalContext context)
  {
    List<Base> gisLayers = context.GetAscendants().Where(IsGISType).Where(obj => obj != context.Current).ToList();
    return gisLayers.Count > 0;
  }

  [Pure]
  private static bool IsGISType(Base obj)
  {
    return obj is RasterLayer or VectorLayer;
  }
}
