using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Speckle.Connectors.Rhino.HostApp;
using Speckle.Connectors.Utils.Builders;
using Speckle.Connectors.Utils.Conversion;
using Speckle.Connectors.Utils.Operations.Receive;
using Speckle.Converters.Common;
using Speckle.Sdk;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.Instances;

namespace Speckle.Connectors.Rhino.Operations.Receive;

/// <summary>
/// <para>Expects to be a scoped dependency per receive operation.</para>
/// </summary>
public class RhinoHostObjectBuilder : IHostObjectBuilder
{
  private readonly IRootToHostConverter _converter;
  private readonly IConversionContextStack<RhinoDoc, UnitSystem> _contextStack;
  private readonly RhinoInstanceBaker _instanceBaker;
  private readonly RhinoLayerManager _layerManager;
  private readonly RhinoMaterialBaker _materialBaker;
  private readonly RhinoColorBaker _colorBaker;

  private readonly RhinoGroupBaker _groupBaker;
  private readonly RootObjectUnpacker _rootObjectUnpacker;

  public RhinoHostObjectBuilder(
    IRootToHostConverter converter,
    IConversionContextStack<RhinoDoc, UnitSystem> contextStack,
    RhinoLayerManager layerManager,
    RootObjectUnpacker rootObjectUnpacker,
    RhinoInstanceBaker instanceBaker,
    RhinoMaterialBaker materialBaker,
    RhinoColorBaker colorBaker,
    RhinoGroupBaker groupBaker
  )
  {
    _converter = converter;
    _contextStack = contextStack;
    _rootObjectUnpacker = rootObjectUnpacker;
    _instanceBaker = instanceBaker;
    _materialBaker = materialBaker;
    _colorBaker = colorBaker;
    _layerManager = layerManager;
    _groupBaker = groupBaker;
  }

  public Task<HostObjectBuilderResult> Build(
    Base rootObject,
    string projectName,
    string modelName,
    Action<string, double?>? onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    using var activity = SpeckleActivityFactory.Start("Build");
    // POC: This is where the top level base-layer name is set. Could be abstracted or injected in the context?
    var baseLayerName = $"Project {projectName}: Model {modelName}";

    // 0 - Clean then Rock n Roll!
    PreReceiveDeepClean(baseLayerName);
    _layerManager.CreateBaseLayer(baseLayerName);

    // 1 - Unpack objects and proxies from root commit object
    var objectsToConvert = _rootObjectUnpacker.GetObjectsToConvert(rootObject);
    var instanceDefinitionProxies = _rootObjectUnpacker.TryGetInstanceDefinitionProxies(rootObject);
    var groupProxies = _rootObjectUnpacker.TryGetGroupProxies(rootObject);
    var renderMaterials = _rootObjectUnpacker.TryGetRenderMaterialProxies(rootObject);
    var colorProxies = _rootObjectUnpacker.TryGetColorProxies(rootObject);

    // 2 - Split atomic objects and instance components with their path
    var (atomicObjects, instanceComponents) = _rootObjectUnpacker.SplitAtomicObjectsAndInstances(objectsToConvert);
    var atomicObjectsWithPath = atomicObjects.Select(o => (_layerManager.GetLayerPath(o), o.Current)).ToList();
    var instanceComponentsWithPath = instanceComponents
      .Select(o => (_layerManager.GetLayerPath(o), (o.Current as IInstanceComponent)!))
      .ToList();

    // POC: these are not captured by traversal, so we need to re-add them here
    if (instanceDefinitionProxies != null && instanceDefinitionProxies.Count > 0)
    {
      var transformed = instanceDefinitionProxies.Select(proxy =>
        (Array.Empty<Collection>(), proxy as IInstanceComponent)
      );
      instanceComponentsWithPath.AddRange(transformed);
    }

    var doc = _contextStack.Current.Document;

    // using (var _ = SpeckleActivityFactory.Start("Traversal"))
    // {
    //   // POC: these are not captured by traversal, so we need to re-add them here
    //   if (instanceDefinitionProxies != null && instanceDefinitionProxies.Count > 0)
    //   {
    //     var transformed = instanceDefinitionProxies.Select(proxy =>
    //       (Array.Empty<Collection>(), proxy as IInstanceComponent)
    //     );
    //     instanceComponents.AddRange(transformed);
    //   }
    //
    //   // Split up the instances from the non-instances
    //   foreach (TraversalContext tc in objectsToConvert)
    //   {
    //     Collection[] collectionPath = _layerManager.GetLayerPath(tc);
    //
    //     if (tc.Current is IInstanceComponent instanceComponent)
    //     {
    //       instanceComponents.Add((collectionPath, instanceComponent));
    //     }
    //     else
    //     {
    //       atomicObjects.Add((collectionPath, tc.Current));
    //     }
    //   }
    // }

    // Stage 0: Bake materials and colors, as they are used later down the line by layers and objects
    onOperationProgressed?.Invoke("Converting materials and colors", null);
    if (renderMaterials != null)
    {
      using var _ = SpeckleActivityFactory.Start("Render Materials");
      _materialBaker.BakeMaterials(renderMaterials, baseLayerName);
    }

    if (colorProxies != null)
    {
      _colorBaker.ParseColors(colorProxies);
    }

    // Stage 0.1: Pre bake layers
    // See [CNX-325: Rhino: Change receive operation order to increase performance](https://linear.app/speckle/issue/CNX-325/rhino-change-receive-operation-order-to-increase-performance)
    onOperationProgressed?.Invoke("Baking layers (redraw disabled)", null);
    using (var _ = SpeckleActivityFactory.Start("Pre baking layers"))
    {
      using var layerNoDraw = new DisableRedrawScope(doc.Views);
      foreach (var (path, _) in atomicObjectsWithPath)
      {
        _layerManager.GetAndCreateLayerFromPath(path, baseLayerName, out bool _);
      }
    }

    // Stage 1: Convert atomic objects
    List<string> bakedObjectIds = new();
    Dictionary<string, List<string>> applicationIdMap = new(); // This map is used in converting blocks in stage 2. keeps track of original app id => resulting new app ids post baking
    List<ReceiveConversionResult> conversionResults = new();

    int count = 0;
    using (var _ = SpeckleActivityFactory.Start("Converting objects"))
    {
      foreach (var (path, obj) in atomicObjectsWithPath)
      {
        onOperationProgressed?.Invoke("Converting objects", (double)++count / atomicObjects.Count);
        try
        {
          // 1: create layer
          int layerIndex = _layerManager.GetAndCreateLayerFromPath(path, baseLayerName, out bool _);

          // 2: convert
          var result = _converter.Convert(obj);

          // 3: bake
          var conversionIds = new List<string>();
          if (result is GeometryBase geometryBase)
          {
            var guid = BakeObject(geometryBase, obj, layerIndex);
            conversionIds.Add(guid.ToString());
          }
          else if (result is IEnumerable<(object, Base)> fallbackConversionResult)
          {
            var guids = BakeObjectsAsGroup(fallbackConversionResult, obj, layerIndex, baseLayerName);
            conversionIds.AddRange(guids.Select(id => id.ToString()));
          }

          if (conversionIds.Count == 0)
          {
            throw new SpeckleConversionException($"Failed to convert object.");
          }

          // 4: log
          var id = conversionIds[0]; // this is group id if it is a one to many conversion, otherwise id of object itself
          conversionResults.Add(new(Status.SUCCESS, obj, id, result.GetType().ToString()));
          if (conversionIds.Count == 1)
          {
            bakedObjectIds.Add(id);
          }
          else
          {
            // first item always a group id if it is a one-to-many,
            // we do not want to deal with later groups and its sub elements. It causes a huge issue on performance.
            bakedObjectIds.AddRange(conversionIds.Skip(1));
          }

          // 5: populate app id map
          applicationIdMap[obj.applicationId ?? obj.id] = conversionIds;
        }
        catch (Exception ex) when (!ex.IsFatal())
        {
          conversionResults.Add(new(Status.ERROR, obj, null, null, ex));
        }
      }
    }

    // Stage 2: Convert instances
    using (var _ = SpeckleActivityFactory.Start("Converting instances"))
    {
      var (createdInstanceIds, consumedObjectIds, instanceConversionResults) = _instanceBaker.BakeInstances(
        instanceComponentsWithPath,
        applicationIdMap,
        baseLayerName,
        onOperationProgressed
      );

      bakedObjectIds.RemoveAll(id => consumedObjectIds.Contains(id)); // remove all objects that have been "consumed"
      bakedObjectIds.AddRange(createdInstanceIds); // add instance ids
      conversionResults.RemoveAll(result => result.ResultId != null && consumedObjectIds.Contains(result.ResultId)); // remove all conversion results for atomic objects that have been consumed (POC: not that cool, but prevents problems on object highlighting)
      conversionResults.AddRange(instanceConversionResults); // add instance conversion results to our list
    }

    // Stage 3: Groups
    if (groupProxies is not null)
    {
      _groupBaker.BakeGroups(groupProxies, applicationIdMap, baseLayerName);
    }

    _contextStack.Current.Document.Views.Redraw();

    return Task.FromResult(new HostObjectBuilderResult(bakedObjectIds, conversionResults));
  }

  private void PreReceiveDeepClean(string baseLayerName)
  {
    // Remove all previously received layers and render materials from the document
    int rootLayerIndex = _contextStack.Current.Document.Layers.Find(Guid.Empty, baseLayerName, RhinoMath.UnsetIntIndex);

    _instanceBaker.PurgeInstances(baseLayerName);
    _materialBaker.PurgeMaterials(baseLayerName);

    var doc = _contextStack.Current.Document;
    // Cleans up any previously received objects
    if (rootLayerIndex != RhinoMath.UnsetIntIndex)
    {
      var documentLayer = doc.Layers[rootLayerIndex];
      var childLayers = documentLayer.GetChildren();
      if (childLayers != null)
      {
        using var layerNoDraw = new DisableRedrawScope(doc.Views);
        foreach (var layer in childLayers)
        {
          var purgeSuccess = doc.Layers.Purge(layer.Index, true);
          if (!purgeSuccess)
          {
            Console.WriteLine($"Failed to purge layer: {layer}");
          }
        }
      }
    }

    // Cleans up any previously received group
    _groupBaker.PurgeGroups(baseLayerName);
  }

  private Guid BakeObject(GeometryBase obj, Base originalObject, int layerIndex)
  {
    ObjectAttributes atts = new() { LayerIndex = layerIndex };
    var objectId = originalObject.applicationId ?? originalObject.id;

    if (_materialBaker.ObjectIdAndMaterialIndexMap.TryGetValue(objectId, out int mIndex))
    {
      atts.MaterialIndex = mIndex;
      atts.MaterialSource = ObjectMaterialSource.MaterialFromObject;
    }

    if (_colorBaker.ObjectColorsIdMap.TryGetValue(objectId, out (Color, ObjectColorSource) color))
    {
      atts.ObjectColor = color.Item1;
      atts.ColorSource = color.Item2;
    }

    return _contextStack.Current.Document.Objects.Add(obj, atts);
  }

  private List<Guid> BakeObjectsAsGroup(
    IEnumerable<(object, Base)> fallbackConversionResult,
    Base originatingObject,
    int layerIndex,
    string baseLayerName
  )
  {
    List<Guid> objectIds = new();
    foreach (var (conversionResult, originalBaseObject) in fallbackConversionResult)
    {
      if (conversionResult is not GeometryBase geometryBase)
      {
        // TODO: throw?
        continue;
      }

      var id = BakeObject(geometryBase, originalBaseObject, layerIndex);
      objectIds.Add(id);
    }

    var groupIndex = _contextStack.Current.Document.Groups.Add(
      $@"{originatingObject.speckle_type.Split('.').Last()} - {originatingObject.applicationId ?? originatingObject.id}  ({baseLayerName})",
      objectIds
    );
    var group = _contextStack.Current.Document.Groups.FindIndex(groupIndex);
    objectIds.Insert(0, group.Id);
    return objectIds;
  }
}
