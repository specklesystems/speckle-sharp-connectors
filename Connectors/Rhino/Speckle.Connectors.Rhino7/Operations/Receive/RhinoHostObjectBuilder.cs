using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Speckle.Connectors.Rhino7.HostApp;
using Speckle.Connectors.Utils.Builders;
using Speckle.Connectors.Utils.Conversion;
using Speckle.Converters.Common;
using Speckle.Core.Logging;
using Speckle.Core.Models;
using Speckle.Core.Models.Collections;
using Speckle.Core.Models.GraphTraversal;
using Speckle.Core.Models.Instances;
using SpeckleRenderMaterial = Objects.Other.RenderMaterial;

namespace Speckle.Connectors.Rhino7.Operations.Receive;

/// <summary>
/// <para>Expects to be a scoped dependency per receive operation.</para>
/// </summary>
public class RhinoHostObjectBuilder : IHostObjectBuilder
{
  private readonly IRootToHostConverter _converter;
  private readonly IConversionContextStack<RhinoDoc, UnitSystem> _contextStack;
  private readonly GraphTraversal _traverseFunction;
  private readonly RhinoInstanceObjectsManager _instanceObjectsManager;
  private readonly RhinoLayerManager _layerManager;
  private readonly RhinoMaterialManager _materialManager;

  public RhinoHostObjectBuilder(
    IRootToHostConverter converter,
    IConversionContextStack<RhinoDoc, UnitSystem> contextStack,
    GraphTraversal traverseFunction,
    RhinoLayerManager layerManager,
    RhinoInstanceObjectsManager instanceObjectsManager,
    RhinoMaterialManager materialManager
  )
  {
    _converter = converter;
    _contextStack = contextStack;
    _traverseFunction = traverseFunction;
    _layerManager = layerManager;
    _instanceObjectsManager = instanceObjectsManager;
    _materialManager = materialManager;
  }

  public HostObjectBuilderResult Build(
    Base rootObject,
    string projectName,
    string modelName,
    Action<string, double?>? onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    // POC: This is where the top level base-layer name is set. Could be abstracted or injected in the context?
    var baseLayerName = $"Project {projectName}: Model {modelName}";

    var objectsToConvert = _traverseFunction
      .TraverseWithProgress(rootObject, onOperationProgressed, cancellationToken)
      .Where(obj => obj.Current is not Collection);

    var instanceDefinitionProxies = (rootObject["instanceDefinitionProxies"] as List<object>)
      ?.Cast<InstanceDefinitionProxy>()
      .ToList();

    var groupProxies = (rootObject["groupProxies"] as List<object>)?.Cast<GroupProxy>().ToList();

    List<SpeckleRenderMaterial>? renderMaterials = (rootObject["renderMaterials"] as List<object>)
      ?.Cast<SpeckleRenderMaterial>()
      .ToList();

    var conversionResults = BakeObjects(
      objectsToConvert,
      instanceDefinitionProxies,
      groupProxies,
      renderMaterials,
      baseLayerName,
      onOperationProgressed
    );

    _contextStack.Current.Document.Views.Redraw();

    return conversionResults;
  }

  private HostObjectBuilderResult BakeObjects(
    IEnumerable<TraversalContext> objectsGraph,
    List<InstanceDefinitionProxy>? instanceDefinitionProxies,
    List<GroupProxy>? groupProxies,
    List<SpeckleRenderMaterial>? renderMaterials,
    string baseLayerName,
    Action<string, double?>? onOperationProgressed
  )
  {
    RhinoDoc doc = _contextStack.Current.Document;

    // Remove all previously received layers and render materials from the document
    int rootLayerIndex = _contextStack.Current.Document.Layers.Find(Guid.Empty, baseLayerName, RhinoMath.UnsetIntIndex);
    PreReceiveDeepClean(baseLayerName, rootLayerIndex);

    _layerManager.CreateBaseLayer(baseLayerName);
    using var noDraw = new DisableRedrawScope(doc.Views);

    // POC: these are not captured by traversal, so we need to re-add them here
    var instanceComponents = new List<(Collection[] collectionPath, IInstanceComponent obj)>();
    if (instanceDefinitionProxies != null && instanceDefinitionProxies.Count > 0)
    {
      var transformed = instanceDefinitionProxies.Select(proxy => (new Collection[] { }, proxy as IInstanceComponent));
      instanceComponents.AddRange(transformed);
    }

    List<(Collection[] collectionPath, Base obj)> atomicObjects = new();

    // Split up the instances from the non-instances
    foreach (TraversalContext tc in objectsGraph)
    {
      Collection[] collectionPath = _layerManager.GetLayerPath(tc);

      if (tc.Current is IInstanceComponent instanceComponent)
      {
        instanceComponents.Add((collectionPath, instanceComponent));
      }
      else
      {
        atomicObjects.Add((collectionPath, tc.Current));
      }
    }

    List<ReceiveConversionResult> conversionResults = new();

    // Stage 0: Convert render materials
    Dictionary<string, int> materialsIdMap = new();
    if (renderMaterials != null && renderMaterials.Count > 0)
    {
      (materialsIdMap, List<ReceiveConversionResult> materialsConversionResults) = _materialManager.BakeMaterials(
        renderMaterials,
        baseLayerName,
        onOperationProgressed
      );
      conversionResults.AddRange(materialsConversionResults); // add instance conversion results to our list
    }

    // Stage 1: Convert atomic objects
    // Note: this can become encapsulated later in an "atomic object baker" of sorts, if needed.
    var bakedObjectIds = new List<string>();
    var applicationIdMap = new Dictionary<string, List<string>>(); // used in converting blocks in stage 2. keeps track of original app id => resulting new app ids post baking
    var count = 0;
    foreach (var (path, obj) in atomicObjects)
    {
      onOperationProgressed?.Invoke("Converting objects", (double)++count / atomicObjects.Count);
      try
      {
        int layerIndex = _layerManager.GetAndCreateLayerFromPath(path, baseLayerName);
        // TODO: need to add render materials to layers
        int materialIndex = obj["renderMaterialId"] is string renderMaterialId
          ? materialsIdMap.ContainsKey(renderMaterialId)
            ? materialsIdMap[renderMaterialId]
            : 0
          : 0;
        var result = _converter.Convert(obj);
        var conversionIds = HandleConversionResult(result, obj, layerIndex, materialIndex).ToList();
        foreach (var r in conversionIds)
        {
          conversionResults.Add(new(Status.SUCCESS, obj, r, result.GetType().ToString()));
          bakedObjectIds.Add(r);
        }

        if (obj.applicationId != null)
        {
          applicationIdMap[obj.applicationId] = conversionIds;
        }
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        conversionResults.Add(new(Status.ERROR, obj, null, null, ex));
      }
    }

    // Stage 2: Convert instances
    var (createdInstanceIds, consumedObjectIds, instanceConversionResults) = _instanceObjectsManager.BakeInstances(
      instanceComponents,
      applicationIdMap,
      baseLayerName,
      onOperationProgressed
    );

    bakedObjectIds.RemoveAll(id => consumedObjectIds.Contains(id)); // remove all objects that have been "consumed"
    bakedObjectIds.AddRange(createdInstanceIds); // add instance ids
    conversionResults.RemoveAll(result => result.ResultId != null && consumedObjectIds.Contains(result.ResultId)); // remove all conversion results for atomic objects that have been consumed (POC: not that cool, but prevents problems on object highlighting)
    conversionResults.AddRange(instanceConversionResults); // add instance conversion results to our list

    // Stage 3: Group
    if (groupProxies is not null)
    {
      foreach (GroupProxy groupProxy in groupProxies.OrderBy(g => g.objects.Count))
      {
        var appIds = groupProxy.objects.SelectMany(oldObjId => applicationIdMap[oldObjId]).Select(id => new Guid(id));
        var index = RhinoDoc.ActiveDoc.Groups.Add(appIds);
        var addedGroup = RhinoDoc.ActiveDoc.Groups.FindIndex(index);
        addedGroup.Name = groupProxy.name;
      }
    }

    // Stage 4: Return
    return new(bakedObjectIds, conversionResults);
  }

  private void PreReceiveDeepClean(string baseLayerName, int rootLayerIndex)
  {
    _instanceObjectsManager.PurgeInstances(baseLayerName);
    _materialManager.PurgeMaterials(baseLayerName);

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
  }

  private IReadOnlyList<string> HandleConversionResult(
    object conversionResult,
    Base originalObject,
    int layerIndex,
    int materialIndex
  )
  {
    var doc = _contextStack.Current.Document;
    List<string> newObjectIds = new();
    switch (conversionResult)
    {
      case IEnumerable<GeometryBase> list:
      {
        Group group = BakeObjectsAsGroup(originalObject.id, list, layerIndex, materialIndex);
        newObjectIds.Add(group.Id.ToString());
        break;
      }
      case GeometryBase newObject:
      {
        Guid newObjectGuid = doc.Objects.Add(
          newObject,
          new ObjectAttributes
          {
            LayerIndex = layerIndex,
            MaterialIndex = materialIndex,
            MaterialSource =
              materialIndex == 0 ? ObjectMaterialSource.MaterialFromLayer : ObjectMaterialSource.MaterialFromObject
          }
        );
        newObjectIds.Add(newObjectGuid.ToString());
        break;
      }
      default:
        throw new SpeckleConversionException(
          $"Unexpected result from conversion: Expected {nameof(GeometryBase)} but instead got {conversionResult.GetType().Name}"
        );
    }

    return newObjectIds;
  }

  private Group BakeObjectsAsGroup(string groupName, IEnumerable<GeometryBase> list, int layerIndex, int materialIndex)
  {
    var doc = _contextStack.Current.Document;
    var objectIds = list.Select(obj =>
      doc.Objects.Add(
        obj,
        new ObjectAttributes
        {
          LayerIndex = layerIndex,
          MaterialIndex = materialIndex,
          MaterialSource =
            materialIndex == 0 ? ObjectMaterialSource.MaterialFromLayer : ObjectMaterialSource.MaterialFromObject
        }
      )
    );
    var groupIndex = _contextStack.Current.Document.Groups.Add(groupName, objectIds);
    var group = _contextStack.Current.Document.Groups.FindIndex(groupIndex);
    return group;
  }
}
