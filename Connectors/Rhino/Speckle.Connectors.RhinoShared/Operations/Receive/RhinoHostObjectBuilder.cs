using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Speckle.Connectors.Rhino.HostApp;
using Speckle.Connectors.Utils.Builders;
using Speckle.Connectors.Utils.Conversion;
using Speckle.Converters.Common;
using Speckle.Core.Logging;
using Speckle.Core.Models;
using Speckle.Core.Models.Collections;
using Speckle.Core.Models.GraphTraversal;
using Speckle.Core.Models.Instances;
using RenderMaterialProxy = Objects.Other.RenderMaterialProxy;

namespace Speckle.Connectors.Rhino.Operations.Receive;

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

    List<RenderMaterialProxy>? renderMaterials = (rootObject["renderMaterialProxies"] as List<object>)
      ?.Cast<RenderMaterialProxy>()
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
    List<RenderMaterialProxy>? materialProxies,
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
    List<(Collection[] collectionPath, IInstanceComponent obj)> instanceComponents = new();
    if (instanceDefinitionProxies != null && instanceDefinitionProxies.Count > 0)
    {
      var transformed = instanceDefinitionProxies.Select(proxy =>
        (Array.Empty<Collection>(), proxy as IInstanceComponent)
      );
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

    // Stage 0: Render Materials
    Dictionary<string, int> objectMaterialsIdMap = new();
    if (materialProxies != null)
    {
      objectMaterialsIdMap = BakeRenderMaterials(
        materialProxies,
        baseLayerName,
        onOperationProgressed,
        conversionResults
      );
    }

    // Stage 1: Convert atomic objects
    // Note: this can become encapsulated later in an "atomic object baker" of sorts, if needed.
    List<string> bakedObjectIds = new();
    Dictionary<string, List<string>> applicationIdMap = new(); // used in converting blocks in stage 2. keeps track of original app id => resulting new app ids post baking
    int count = 0;

    foreach (var (path, obj) in atomicObjects)
    {
      onOperationProgressed?.Invoke("Converting objects", (double)++count / atomicObjects.Count);
      try
      {
        // POC: it's messy creating layers while in the obj loop, need to set layer material here
        int layerIndex = _layerManager.GetAndCreateLayerFromPath(path, baseLayerName, out bool isNewLayer);
        if (isNewLayer)
        {
          string collectionId = path[^1].applicationId ?? path[^1].id;
          if (objectMaterialsIdMap.TryGetValue(collectionId, out int lIndex))
          {
            doc.Layers[layerIndex].RenderMaterialIndex = lIndex;
          }
        }

        var result = _converter.Convert(obj);
        string objectId = obj.applicationId ?? obj.id; // POC: assuming objects have app ids for this to work?
        int objMaterialIndex = objectMaterialsIdMap.TryGetValue(objectId, out int oIndex) ? oIndex : 0;
        var conversionIds = HandleConversionResult(result, obj, layerIndex, objMaterialIndex).ToList();
        foreach (var r in conversionIds)
        {
          conversionResults.Add(new(Status.SUCCESS, obj, r, result.GetType().ToString()));
          bakedObjectIds.Add(r);
        }

        applicationIdMap[objectId] = conversionIds;
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        conversionResults.Add(new(Status.ERROR, obj, null, null, ex));
      }
    }

    // Stage 2: Convert instances
    var (createdInstanceIds, consumedObjectIds, instanceConversionResults) = BakeInstances(
      instanceComponents,
      applicationIdMap,
      objectMaterialsIdMap,
      baseLayerName,
      onOperationProgressed
    );

    bakedObjectIds.RemoveAll(id => consumedObjectIds.Contains(id)); // remove all objects that have been "consumed"
    bakedObjectIds.AddRange(createdInstanceIds); // add instance ids
    conversionResults.RemoveAll(result => result.ResultId != null && consumedObjectIds.Contains(result.ResultId)); // remove all conversion results for atomic objects that have been consumed (POC: not that cool, but prevents problems on object highlighting)
    conversionResults.AddRange(instanceConversionResults); // add instance conversion results to our list

    // Stage 3: Groups
    if (groupProxies is not null)
    {
      BakeGroups(groupProxies, applicationIdMap);
    }

    // Stage 4: Return
    return new(bakedObjectIds, conversionResults);
  }

  private Dictionary<string, int> BakeRenderMaterials(
    List<RenderMaterialProxy> materialProxies,
    string baseLayerName,
    Action<string, double?>? onOperationProgressed,
    List<ReceiveConversionResult> conversionResults
  )
  {
    // keeps track of the material id to created index in the materials table
    Dictionary<string, int> materialsIdMap = new();

    (materialsIdMap, List<ReceiveConversionResult> materialsConversionResults) = _materialManager.BakeMaterials(
      materialProxies.Select(o => o.value).ToList(),
      baseLayerName,
      onOperationProgressed
    );

    conversionResults.AddRange(materialsConversionResults); // add render material conversion results to our list

    // keeps track of the object id to material index
    Dictionary<string, int> objectMaterialsIdMap = new();
    foreach (RenderMaterialProxy materialProxy in materialProxies)
    {
      string materialId = materialProxy.value.applicationId ?? materialProxy.value.id;
      foreach (string objectId in materialProxy.objects)
      {
        if (materialsIdMap.TryGetValue(materialId, out int materialIndex))
        {
          if (!objectMaterialsIdMap.ContainsKey(objectId))
          {
            objectMaterialsIdMap.Add(objectId, materialIndex);
          }
        }
      }
    }

    return objectMaterialsIdMap;
  }

  private (
    List<string> createdInstanceIds,
    List<string> consumedObjectIds,
    List<ReceiveConversionResult> instanceConversionResults
  ) BakeInstances(
    List<(Collection[] collectionPath, IInstanceComponent obj)> instanceComponents,
    Dictionary<string, List<string>> applicationIdMap,
    Dictionary<string, int> materialIdMap,
    string baseLayerName,
    Action<string, double?>? onOperationProgressed
  )
  {
    var (createdInstanceIds, consumedObjectIds, instanceConversionResults) = _instanceObjectsManager.BakeInstances(
      instanceComponents,
      applicationIdMap,
      baseLayerName,
      onOperationProgressed
    );

    // add materials to created instances
    foreach (IInstanceComponent instanceComponent in instanceComponents.Select(o => o.obj).ToList())
    {
      if (instanceComponent is InstanceProxy instanceProxy)
      {
        string instanceProxyId = instanceProxy.applicationId ?? instanceProxy.id;
        if (createdInstanceIds.Contains(instanceProxyId))
        {
          int instanceMaterialIndex = materialIdMap.TryGetValue(instanceProxyId, out int iIndex) ? iIndex : 0;
          if (instanceMaterialIndex != 0)
          {
            RhinoObject createdInstance = RhinoDoc.ActiveDoc.Objects.FindId(new Guid(instanceProxyId));
            createdInstance.Attributes.MaterialIndex = instanceMaterialIndex;
            createdInstance.Attributes.MaterialSource = ObjectMaterialSource.MaterialFromObject;
          }
        }
      }
    }
    return (createdInstanceIds, consumedObjectIds, instanceConversionResults);
  }

  private void BakeGroups(List<GroupProxy> groupProxies, Dictionary<string, List<string>> applicationIdMap)
  {
    foreach (GroupProxy groupProxy in groupProxies.OrderBy(g => g.objects.Count))
    {
      var appIds = groupProxy.objects.SelectMany(oldObjId => applicationIdMap[oldObjId]).Select(id => new Guid(id));
      var index = RhinoDoc.ActiveDoc.Groups.Add(appIds);
      var addedGroup = RhinoDoc.ActiveDoc.Groups.FindIndex(index);
      addedGroup.Name = groupProxy.name;
    }
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
    int materialIndex = 0
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
        ObjectAttributes atts = new() { LayerIndex = layerIndex, MaterialIndex = materialIndex };
        if (materialIndex != 0)
        {
          atts.MaterialSource = ObjectMaterialSource.MaterialFromObject;
        }

        Guid newObjectGuid = doc.Objects.Add(newObject, atts);
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

  private Group BakeObjectsAsGroup(
    string groupName,
    IEnumerable<GeometryBase> list,
    int layerIndex,
    int materialIndex = 0
  )
  {
    var doc = _contextStack.Current.Document;
    List<Guid> objectIds = new();
    foreach (GeometryBase obj in list)
    {
      ObjectAttributes atts = new() { LayerIndex = layerIndex, MaterialIndex = materialIndex };
      if (materialIndex != 0)
      {
        atts.MaterialSource = ObjectMaterialSource.MaterialFromObject;
      }

      objectIds.Add(doc.Objects.Add(obj, atts));
    }

    var groupIndex = _contextStack.Current.Document.Groups.Add(groupName, objectIds);
    var group = _contextStack.Current.Document.Groups.FindIndex(groupIndex);
    return group;
  }
}
