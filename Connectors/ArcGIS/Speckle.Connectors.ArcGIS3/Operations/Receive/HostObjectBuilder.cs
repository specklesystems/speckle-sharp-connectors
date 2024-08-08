using System.Diagnostics.Contracts;
using System.Drawing;
using ArcGIS.Core.CIM;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using Objects.GIS;
using Objects.Other;
using Speckle.Connectors.ArcGIS.Utils;
using Speckle.Connectors.Utils.Builders;
using Speckle.Connectors.Utils.Conversion;
using Speckle.Connectors.Utils.Instances;
using Speckle.Converters.ArcGIS3;
using Speckle.Converters.ArcGIS3.Utils;
using Speckle.Converters.Common;
using Speckle.Objects.GIS;
using Speckle.Sdk;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.GraphTraversal;
using Speckle.Sdk.Models.Instances;
using RasterLayer = Speckle.Objects.GIS.RasterLayer;

namespace Speckle.Connectors.ArcGIS.Operations.Receive;

public class ArcGISHostObjectBuilder : IHostObjectBuilder
{
  private readonly IRootToHostConverter _converter;
  private readonly INonNativeFeaturesUtils _nonGisFeaturesUtils;
  private readonly ILocalToGlobalUnpacker _localToGlobalUnpacker;
  private readonly ILocalToGlobalConverterUtils _localToGlobalConverterUtils;

  // POC: figure out the correct scope to only initialize on Receive
  private readonly IConversionContextStack<ArcGISDocument, Unit> _contextStack;
  private readonly GraphTraversal _traverseFunction;

  public ArcGISHostObjectBuilder(
    IRootToHostConverter converter,
    IConversionContextStack<ArcGISDocument, Unit> contextStack,
    INonNativeFeaturesUtils nonGisFeaturesUtils,
    ILocalToGlobalUnpacker localToGlobalUnpacker,
    ILocalToGlobalConverterUtils localToGlobalConverterUtils,
    GraphTraversal traverseFunction
  )
  {
    _converter = converter;
    _contextStack = contextStack;
    _nonGisFeaturesUtils = nonGisFeaturesUtils;
    _localToGlobalUnpacker = localToGlobalUnpacker;
    _localToGlobalConverterUtils = localToGlobalConverterUtils;
    _traverseFunction = traverseFunction;
  }

  public async Task<HostObjectBuilderResult> Build(
    Base rootObject,
    string projectName,
    string modelName,
    Action<string, double?>? onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    // TODO get spatialRef and offsets & rotation from ProjectInfo in CommitObject
    // ATM, GIS commit CRS is stored per layer (in FeatureClass converter), but should be moved to the Root level too

    // Prompt the UI conversion started. Progress bar will swoosh.
    onOperationProgressed?.Invoke("Converting", null);

    var objectsToConvertTc = _traverseFunction
      .Traverse(rootObject)
      .Where(ctx => ctx.Current is not Collection || IsGISType(ctx.Current))
      .Where(ctx => HasGISParent(ctx) is false)
      .ToList();

    // get all materials

    List<RenderMaterialProxy>? renderMaterials = (rootObject["renderMaterialProxies"] as List<object>)
      ?.Cast<RenderMaterialProxy>()
      .ToList();
    if (renderMaterials != null)
    {
      _contextStack.Current.Document.RenderMaterialProxies = renderMaterials;
    }
    var instanceDefinitionProxies = (rootObject["instanceDefinitionProxies"] as List<object>)
      ?.Cast<InstanceDefinitionProxy>()
      .ToList();

    var objectsToConvert = _localToGlobalUnpacker.Unpack(instanceDefinitionProxies, objectsToConvertTc);

    int allCount = objectsToConvert.Count;
    int count = 0;
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
        if (IsGISType(obj))
        {
          string nestedLayerPath = $"{string.Join("\\", path)}";
          string datasetId = await QueuedTask.Run(() => (string)_converter.Convert(obj)).ConfigureAwait(false);
          conversionTracker[objectToConvert.TraversalContext] = new ObjectConversionTracker(
            obj,
            nestedLayerPath,
            datasetId
          );
        }
        else
        {
          obj = _localToGlobalConverterUtils.TransformObjects(objectToConvert.AtomicObject, objectToConvert.Matrix);

          string nestedLayerPath = $"{string.Join("\\", path)}\\{obj.speckle_type.Split(".")[^1]}";
          Geometry converted = await QueuedTask.Run(() => (Geometry)_converter.Convert(obj)).ConfigureAwait(false);

          conversionTracker[objectToConvert.TraversalContext] = new ObjectConversionTracker(
            obj,
            nestedLayerPath,
            converted
          );
        }
      }
      catch (Exception ex) when (!ex.IsFatal()) // DO NOT CATCH SPECIFIC STUFF, conversion errors should be recoverable
      {
        results.Add(new(Status.ERROR, obj, null, null, ex));
      }

      onOperationProgressed?.Invoke("Converting", (double)++count / allCount);
    }

    // 2. convert Database entries with non-GIS geometry datasets
    onOperationProgressed?.Invoke("Writing to Database", null);
    await QueuedTask
      .Run(() =>
      {
        _nonGisFeaturesUtils.WriteGeometriesToDatasets(conversionTracker, onOperationProgressed);
      })
      .ConfigureAwait(false);

    // Create placeholder for Group Layers
    Dictionary<string, GroupLayer> createdLayerGroups = new();

    // 3. add layer and tables to the Table Of Content
    int bakeCount = 0;
    Dictionary<string, MapMember> bakedMapMembers = new();
    onOperationProgressed?.Invoke("Adding to Map", bakeCount);

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
      else if (bakedMapMembers.TryGetValue(trackerItem.DatasetId, out MapMember? value))
      {
        // add layer and layer URI to tracker
        trackerItem.AddConvertedMapMember(value);
        trackerItem.AddLayerURI(value.URI);
        conversionTracker[item.Key] = trackerItem; // not necessary atm, but needed if we use conversionTracker further
        // only add a report item
        AddResultsFromTracker(trackerItem, results);
        AddColorCategory(trackerItem);
      }
      else
      {
        // add layer to Map
        MapMember mapMember = await AddDatasetsToMap(trackerItem, createdLayerGroups, projectName, modelName)
          .ConfigureAwait(false);

        // add layer and layer URI to tracker
        trackerItem.AddConvertedMapMember(mapMember);
        trackerItem.AddLayerURI(mapMember.URI);
        conversionTracker[item.Key] = trackerItem; // not necessary atm, but needed if we use conversionTracker further

        // add layer URI to bakedIds
        bakedObjectIds.Add(trackerItem.MappedLayerURI == null ? "" : trackerItem.MappedLayerURI);

        // mark dataset as already created
        bakedMapMembers[trackerItem.DatasetId] = mapMember;

        // add report item
        AddResultsFromTracker(trackerItem, results);
        AddColorCategory(trackerItem);
      }

      onOperationProgressed?.Invoke("Adding to Map", (double)++bakeCount / conversionTracker.Count);
    }

    bakedObjectIds.AddRange(createdLayerGroups.Values.Select(x => x.URI));

    // TODO: validated a correct set regarding bakedobject ids
    return new(bakedObjectIds, results);
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

  private Task<MapMember> AddDatasetsToMap(
    ObjectConversionTracker trackerItem,
    Dictionary<string, GroupLayer> createdLayerGroups,
    string projectName,
    string modelName
  )
  {
    return QueuedTask.Run(() =>
    {
      // get layer details
      string? datasetId = trackerItem.DatasetId; // should not be null here
      Uri uri =
        new($"{_contextStack.Current.Document.SpeckleDatabasePath.AbsolutePath.Replace('/', '\\')}\\{datasetId}");
      string nestedLayerName = trackerItem.NestedLayerName;

      // add group for the current layer
      string shortName = nestedLayerName.Split("\\")[^1];
      string nestedLayerPath = string.Join("\\", nestedLayerName.Split("\\").SkipLast(1));

      // if no general group layer found
      if (createdLayerGroups.Count == 0)
      {
        Map map = _contextStack.Current.Document.Map;
        GroupLayer mainGroupLayer = LayerFactory.Instance.CreateGroupLayer(map, 0, $"{projectName}: {modelName}");
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

        // if Scene
        // https://community.esri.com/t5/arcgis-pro-sdk-questions/sdk-equivalent-to-changing-layer-s-elevation/td-p/1346139
        if (_contextStack.Current.Document.Map.IsScene)
        {
          var groundSurfaceLayer = _contextStack.Current.Document.Map.GetGroundElevationSurfaceLayer();
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

        layer.SetExpanded(true);
        if (layer is FeatureLayer fLayer)
        {
          SetLayerRenderer(fLayer, null);
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
    });
  }

  private void SetLayerRenderer(FeatureLayer fLayer, CIMUniqueValueClass? newUniqueValueClass)
  {
    var color = Color.FromArgb(ColorFactory.Instance.GreyRGB.CIMColorToInt());
    CIMSymbolReference defaultSymbol = CreateSymbol(fLayer, color);

    // get renderer classes, add existing, create new is needed
    List<CIMUniqueValueClass> listUniqueValueClasses = new() { };

    var existingRenderer = fLayer.GetRenderer();
    if (existingRenderer is CIMUniqueValueRenderer uniqueRenderer)
    {
      if (uniqueRenderer.Groups[0].Classes != null)
      {
        listUniqueValueClasses.AddRange(uniqueRenderer.Groups[0].Classes.ToList());
      }
    }

    // Add new CIMUniqueValueClass
    if (newUniqueValueClass != null)
    {
      listUniqueValueClasses.Add(newUniqueValueClass);
    }

    //Create a list of CIMUniqueValueGroup
    CIMUniqueValueGroup uvg = new() { Classes = listUniqueValueClasses.ToArray(), };

    List<CIMUniqueValueGroup> listUniqueValueGroups = new() { uvg };

    //Create the CIMUniqueValueRenderer
    CIMUniqueValueRenderer uvr =
      new()
      {
        UseDefaultSymbol = true,
        DefaultLabel = "all other values",
        DefaultSymbol = defaultSymbol,
        Groups = listUniqueValueGroups.ToArray(),
        Fields = new string[] { "Speckle_ID" }
      };

    //Set the feature layer's renderer.
    fLayer.SetRenderer(uvr);
  }

  private void AddColorCategory(ObjectConversionTracker trackerItem)
  {
    // get color
    Color color = Color.FromArgb(255, 255, 255, 255);
    foreach (var materialProxy in _contextStack.Current.Document.RenderMaterialProxies)
    {
      if (trackerItem.Base.applicationId is string appId && materialProxy.objects.Contains(appId))
      {
        color = Color.FromArgb(materialProxy.value.diffuse);
        break;
      }
    }

    if (trackerItem.HostAppMapMember is FeatureLayer fLyr)
    {
      CIMSymbolReference symbol = CreateSymbol(fLyr, color);

      // First create a "CIMUniqueValueClass" for the cities in Alabama.
      List<CIMUniqueValue> listUniqueValues =
        new() { new CIMUniqueValue { FieldValues = new string[] { trackerItem.Base.id } } };

      CIMUniqueValueClass newUniqueValueClass =
        new()
        {
          Editable = true,
          Label = trackerItem.Base.id,
          Patch = PatchShape.Default,
          Symbol = symbol,
          Visible = true,
          Values = listUniqueValues.ToArray()
        };

      if (trackerItem.HostAppMapMember is FeatureLayer fLayer)
      {
        SetLayerRenderer(fLayer, newUniqueValueClass);
      }
    }
  }

  public CIMSymbolReference CreateSymbol(FeatureLayer fLyr, Color color)
  {
    var symbol = SymbolFactory
      .Instance.ConstructPointSymbol(ColorFactory.Instance.CreateColor(color))
      .MakeSymbolReference();
    var speckleGeometryType = fLyr.ShapeType;
    if (
      speckleGeometryType is esriGeometryType.esriGeometryLine
      || speckleGeometryType is esriGeometryType.esriGeometryPolyline
    )
    {
      symbol = SymbolFactory
        .Instance.ConstructLineSymbol(ColorFactory.Instance.CreateColor(color))
        .MakeSymbolReference();
    }
    else if (speckleGeometryType is esriGeometryType.esriGeometryPolygon)
    {
      symbol = SymbolFactory
        .Instance.ConstructPolygonSymbol(ColorFactory.Instance.CreateColor(color))
        .MakeSymbolReference();
    }
    else if (speckleGeometryType is esriGeometryType.esriGeometryMultiPatch)
    {
      symbol = SymbolFactory
        .Instance.ConstructPolygonSymbol(ColorFactory.Instance.CreateColor(color))
        .MakeSymbolReference();
    }

    return symbol;
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
