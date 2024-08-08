using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Speckle.Connectors.Rhino.HostApp;
using Speckle.Connectors.Utils.Builders;
using Speckle.Connectors.Utils.Conversion;
using Speckle.Connectors.Utils.Operations;
using Speckle.Converters.Common;
using Speckle.Sdk;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.GraphTraversal;
using Speckle.Sdk.Models.Instances;
using Speckle.Sdk.Models.Proxies;
using RenderMaterialProxy = Speckle.Objects.Other.RenderMaterialProxy;

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
  private readonly RhinoColorManager _colorManager;
  private readonly ISyncToThread _syncToThread;

  public RhinoHostObjectBuilder(
    IRootToHostConverter converter,
    IConversionContextStack<RhinoDoc, UnitSystem> contextStack,
    GraphTraversal traverseFunction,
    RhinoLayerManager layerManager,
    RhinoInstanceObjectsManager instanceObjectsManager,
    RhinoMaterialManager materialManager,
    RhinoColorManager colorManager,
    ISyncToThread syncToThread
  )
  {
    _converter = converter;
    _contextStack = contextStack;
    _traverseFunction = traverseFunction;
    _layerManager = layerManager;
    _instanceObjectsManager = instanceObjectsManager;
    _materialManager = materialManager;
    _colorManager = colorManager;
    _syncToThread = syncToThread;
  }

  public Task<HostObjectBuilderResult> Build(
    Base rootObject,
    string projectName,
    string modelName,
    Action<string, double?>? onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    return _syncToThread.RunOnThread(() =>
    {
      using var activity = SpeckleActivityFactory.Start("Build");
      // POC: This is where the top level base-layer name is set. Could be abstracted or injected in the context?
      var baseLayerName = $"Project {projectName}: Model {modelName}";

      var objectsToConvert = _traverseFunction.Traverse(rootObject).Where(obj => obj.Current is not Collection);
      var instanceDefinitionProxies = (rootObject["instanceDefinitionProxies"] as List<object>)
        ?.Cast<InstanceDefinitionProxy>()
        .ToList();

      var groupProxies = (rootObject["groupProxies"] as List<object>)?.Cast<GroupProxy>().ToList();

      List<RenderMaterialProxy>? renderMaterials = (rootObject["renderMaterialProxies"] as List<object>)
        ?.Cast<RenderMaterialProxy>()
        .ToList();

      List<ColorProxy>? colors = (rootObject["colorProxies"] as List<object>)?.Cast<ColorProxy>().ToList();

      var conversionResults = BakeObjects(
        objectsToConvert,
        instanceDefinitionProxies,
        groupProxies,
        renderMaterials,
        colors,
        baseLayerName,
        onOperationProgressed
      );

      _contextStack.Current.Document.Views.Redraw();

      return conversionResults;
    });
  }

  private HostObjectBuilderResult BakeObjects(
    IEnumerable<TraversalContext> objectsGraph,
    List<InstanceDefinitionProxy>? instanceDefinitionProxies,
    List<GroupProxy>? groupProxies,
    List<RenderMaterialProxy>? materialProxies,
    List<ColorProxy>? colorProxies,
    string baseLayerName,
    Action<string, double?>? onOperationProgressed
  )
  {
    List<(Collection[] collectionPath, Base obj)> atomicObjects = new();
    RhinoDoc doc = _contextStack.Current.Document;
    List<(Collection[] collectionPath, IInstanceComponent obj)> instanceComponents = new();

    using (var _ = SpeckleActivityFactory.Start("Traversal"))
    {
      PreReceiveDeepClean(baseLayerName);
      _layerManager.CreateBaseLayer(baseLayerName);

      // POC: these are not captured by traversal, so we need to re-add them here
      if (instanceDefinitionProxies != null && instanceDefinitionProxies.Count > 0)
      {
        var transformed = instanceDefinitionProxies.Select(proxy =>
          (Array.Empty<Collection>(), proxy as IInstanceComponent)
        );
        instanceComponents.AddRange(transformed);
      }

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
    }

    // Stage 0: Bake materials and colors, as they are used later down the line by layers and objects
    onOperationProgressed?.Invoke("Converting materials and colors", null);
    if (materialProxies != null)
    {
      _materialManager.BakeMaterials(materialProxies, baseLayerName);
    }

    if (colorProxies != null)
    {
      _colorManager.ParseColors(colorProxies);
    }

    // Stage 1: Convert atomic objects
    List<string> bakedObjectIds = new();
    Dictionary<string, List<string>> applicationIdMap = new(); // This map is used in converting blocks in stage 2. keeps track of original app id => resulting new app ids post baking
    List<ReceiveConversionResult> conversionResults = new();

    int count = 0;
    using (var _ = SpeckleActivityFactory.Start("Converting objects"))
    {
      foreach (var (path, obj) in atomicObjects)
      {
        onOperationProgressed?.Invoke("Converting objects", (double)++count / atomicObjects.Count);
        try
        {
          // Convert and bake an object:
          // 1: create layer
          int layerIndex = _layerManager.GetAndCreateLayerFromPath(path, baseLayerName, out bool _);

          // 2: convert
          var result = _converter.Convert(obj);

          var objectId = obj.applicationId ?? obj.id; // POC: assuming objects have app ids for this to work?

          // 3: colors and materials
          var matIndex = _materialManager.ObjectIdAndMaterialIndexMap.TryGetValue(objectId, out int mIndex)
            ? mIndex
            : 0;
          Color? objColor = _colorManager.ObjectColorsIdMap.TryGetValue(objectId, out Color color) ? color : null;

          // 4: actually bake
          var conversionIds = HandleConversionResult(result, obj, layerIndex, matIndex, objColor).ToList();
          foreach (var r in conversionIds)
          {
            conversionResults.Add(new(Status.SUCCESS, obj, r, result.GetType().ToString()));
            bakedObjectIds.Add(r);
          }

          // 5: populate app id map
          applicationIdMap[objectId] = conversionIds;
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
    }

    // Stage 3: Groups
    if (groupProxies is not null)
    {
      using (var _ = SpeckleActivityFactory.Start("Converting groups"))
      {
        BakeGroups(groupProxies, applicationIdMap);
      }
    }

    // Stage 4: Return
    return new(bakedObjectIds, conversionResults);
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

  private void PreReceiveDeepClean(string baseLayerName)
  {
    // Remove all previously received layers and render materials from the document
    int rootLayerIndex = _contextStack.Current.Document.Layers.Find(Guid.Empty, baseLayerName, RhinoMath.UnsetIntIndex);

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
    int? materialIndex = null,
    Color? color = null
  )
  {
    var doc = _contextStack.Current.Document;
    List<string> newObjectIds = new();
    switch (conversionResult)
    {
      case IEnumerable<GeometryBase> list:
      {
        Group group = BakeObjectsAsGroup(originalObject.id, list, layerIndex, materialIndex, color);
        newObjectIds.Add(group.Id.ToString());
        break;
      }
      case GeometryBase newObject:
      {
        ObjectAttributes atts = new() { LayerIndex = layerIndex };
        if (materialIndex is int index)
        {
          atts.MaterialIndex = index;
          atts.MaterialSource = ObjectMaterialSource.MaterialFromObject;
        }

        if (color is Color objColor)
        {
          atts.ObjectColor = objColor;
          atts.ColorSource = ObjectColorSource.ColorFromObject;
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
    int? materialIndex = null,
    Color? color = null
  )
  {
    var doc = _contextStack.Current.Document;
    List<Guid> objectIds = new();
    foreach (GeometryBase obj in list)
    {
      ObjectAttributes atts = new() { LayerIndex = layerIndex };
      if (materialIndex is int index)
      {
        atts.MaterialIndex = index;
        atts.MaterialSource = ObjectMaterialSource.MaterialFromObject;
      }

      if (color is Color objColor)
      {
        atts.ObjectColor = objColor;
        atts.ColorSource = ObjectColorSource.ColorFromObject;
      }

      objectIds.Add(doc.Objects.Add(obj, atts));
    }

    int groupIndex = _contextStack.Current.Document.Groups.Add(groupName, objectIds);
    var group = _contextStack.Current.Document.Groups.FindIndex(groupIndex);
    return group;
  }
}
