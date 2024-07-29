using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Speckle.Connectors.Rhino.Extensions;
using Speckle.Connectors.Utils.Conversion;
using Speckle.Connectors.Utils.Instances;
using Speckle.Core.Common;
using Speckle.Core.Kits;
using Speckle.Core.Logging;
using Speckle.Core.Models;
using Speckle.Core.Models.Collections;
using Speckle.Core.Models.Instances;
using Speckle.DoubleNumerics;

namespace Speckle.Connectors.Rhino.HostApp;

/// <summary>
///  Expects to be a scoped dependency per send or receive operation.
/// POC: Split later unpacker and baker.
/// </summary>
public class RhinoInstanceObjectsManager : IInstanceUnpacker<RhinoObject>, IInstanceBaker<List<string>>
{
  private readonly RhinoLayerManager _layerManager;
  private readonly IInstanceObjectsManager<RhinoObject, List<string>> _instanceObjectsManager;

  public RhinoInstanceObjectsManager(
    RhinoLayerManager layerManager,
    IInstanceObjectsManager<RhinoObject, List<string>> instanceObjectsManager
  )
  {
    _layerManager = layerManager;
    _instanceObjectsManager = instanceObjectsManager;
  }

  public UnpackResult<RhinoObject> UnpackSelection(IEnumerable<RhinoObject> objects)
  {
    foreach (var obj in objects)
    {
      if (obj is InstanceObject instanceObject)
      {
        UnpackInstance(instanceObject);
      }
      _instanceObjectsManager.AddAtomicObject(obj.Id.ToString(), obj);
    }
    return _instanceObjectsManager.GetUnpackResult();
  }

  private void UnpackInstance(InstanceObject instance, int depth = 0)
  {
    var instanceId = instance.Id.ToString();
    var instanceDefinitionId = instance.InstanceDefinition.Id.ToString();
    var currentDoc = RhinoDoc.ActiveDoc; // POC: too much right now to interface around

    InstanceProxy instanceProxy =
      new()
      {
        applicationId = instanceId,
        definitionId = instance.InstanceDefinition.Id.ToString(),
        transform = XFormToMatrix(instance.InstanceXform),
        maxDepth = depth,
        units = currentDoc.ModelUnitSystem.ToSpeckleString()
      };
    _instanceObjectsManager.AddInstanceProxy(instanceId, instanceProxy);

    // For each block instance that has the same definition, we need to keep track of the "maximum depth" at which is found.
    // This will enable on receive to create them in the correct order (descending by max depth, interleaved definitions and instances).
    // We need to interleave the creation of definitions and instances, as some definitions may depend on instances.
    if (
      !_instanceObjectsManager.TryGetInstanceProxiesFromDefinitionId(
        instanceDefinitionId,
        out List<InstanceProxy> instanceProxiesWithSameDefinition
      )
    )
    {
      instanceProxiesWithSameDefinition = new List<InstanceProxy>();
      _instanceObjectsManager.AddInstanceProxiesByDefinitionId(instanceDefinitionId, instanceProxiesWithSameDefinition);
    }

    // We ensure that all previous instance proxies that have the same definition are at this max depth. I kind of have a feeling this can be done more elegantly, but YOLO
    foreach (var instanceProxyWithSameDefinition in instanceProxiesWithSameDefinition)
    {
      if (instanceProxyWithSameDefinition.maxDepth < depth)
      {
        instanceProxyWithSameDefinition.maxDepth = depth;
      }
    }

    instanceProxiesWithSameDefinition.Add(_instanceObjectsManager.GetInstanceProxy(instanceId));

    if (_instanceObjectsManager.TryGetInstanceDefinitionProxy(instanceDefinitionId, out InstanceDefinitionProxy value))
    {
      int depthDifference = depth - value.maxDepth;
      if (depthDifference > 0)
      {
        // all MaxDepth of children definitions and its instances should be increased with difference of depth
        _instanceObjectsManager.UpdateChildrenMaxDepth(value, depthDifference);
      }
      return;
    }

    var definition = new InstanceDefinitionProxy
    {
      applicationId = instanceDefinitionId,
      objects = new List<string>(),
      maxDepth = depth,
      name = instance.InstanceDefinition.Name,
      ["description"] = instance.InstanceDefinition.Description
    };

    _instanceObjectsManager.AddDefinitionProxy(instance.InstanceDefinition.Id.ToString(), definition);

    foreach (var obj in instance.InstanceDefinition.GetObjects())
    {
      definition.objects.Add(obj.Id.ToString());
      if (obj is InstanceObject localInstance)
      {
        UnpackInstance(localInstance, depth + 1);
      }
      _instanceObjectsManager.AddAtomicObject(obj.Id.ToString(), obj);
    }
  }

  /// <summary>
  /// Bakes in the host app doc instances. Assumes constituent atomic objects already present in the host app.
  /// </summary>
  /// <param name="instanceComponents">Instance definitions and instances that need creating.</param>
  /// <param name="applicationIdMap">A dict mapping { original application id -> [resulting application ids post conversion] }</param>
  /// <param name="onOperationProgressed"></param>
  public BakeResult BakeInstances(
    List<(Collection[] collectionPath, IInstanceComponent obj)> instanceComponents,
    Dictionary<string, List<string>> applicationIdMap,
    string baseLayerName,
    Action<string, double?>? onOperationProgressed
  )
  {
    // var doc = _contextStack.Current.Document;
    var doc = RhinoDoc.ActiveDoc; // POC: too much right now to interface around

    var sortedInstanceComponents = instanceComponents
      .OrderByDescending(x => x.obj.maxDepth) // Sort by max depth, so we start baking from the deepest element first
      .ThenBy(x => x.obj is InstanceDefinitionProxy ? 0 : 1) // Ensure we bake the deepest definition first, then any instances that depend on it
      .ToList();
    var definitionIdAndApplicationIdMap = new Dictionary<string, int>();

    var count = 0;
    var conversionResults = new List<ReceiveConversionResult>();
    var createdObjectIds = new List<string>();
    var consumedObjectIds = new List<string>();
    foreach (var (layerCollection, instanceOrDefinition) in sortedInstanceComponents)
    {
      onOperationProgressed?.Invoke("Converting blocks", (double)++count / sortedInstanceComponents.Count);
      try
      {
        if (instanceOrDefinition is InstanceDefinitionProxy definitionProxy)
        {
          var currentApplicationObjectsIds = definitionProxy
            .objects.Select(x => applicationIdMap.TryGetValue(x, out List<string>? value) ? value : null)
            .Where(x => x is not null)
            .SelectMany(id => id.NotNull())
            .ToList();

          var definitionGeometryList = new List<GeometryBase>();
          var attributes = new List<ObjectAttributes>();

          foreach (var id in currentApplicationObjectsIds)
          {
            var docObject = doc.Objects.FindId(new Guid(id));
            definitionGeometryList.Add(docObject.Geometry);
            attributes.Add(docObject.Attributes);
          }

          // POC: Currently we're relying on the definition name for identification if it's coming from speckle and from which model; could we do something else?
          var defName = $"{definitionProxy.name}-({definitionProxy.applicationId})-{baseLayerName}";
          var defIndex = doc.InstanceDefinitions.Add(
            defName,
            "No description", // POC: perhaps bring it along from source? We'd need to look at ACAD first
            Point3d.Origin,
            definitionGeometryList,
            attributes
          );

          // POC: check on defIndex -1, means we haven't created anything - this is most likely an recoverable error at this stage
          if (defIndex == -1)
          {
            throw new ConversionException("Failed to create an instance defintion object.");
          }

          if (definitionProxy.applicationId != null)
          {
            definitionIdAndApplicationIdMap[definitionProxy.applicationId] = defIndex;
          }

          // Rhino deletes original objects on block creation - we should do the same.
          doc.Objects.Delete(currentApplicationObjectsIds.Select(stringId => new Guid(stringId)), false);
          consumedObjectIds.AddRange(currentApplicationObjectsIds);
          createdObjectIds.RemoveAll(id => consumedObjectIds.Contains(id)); // in case we've consumed some existing instances
        }

        if (
          instanceOrDefinition is InstanceProxy instanceProxy
          && definitionIdAndApplicationIdMap.TryGetValue(instanceProxy.definitionId, out int index)
        )
        {
          var transform = MatrixToTransform(instanceProxy.transform, instanceProxy.units);

          // POC: having layer creation during instance bake means no render materials!!
          int layerIndex = _layerManager.GetAndCreateLayerFromPath(layerCollection, baseLayerName, out bool _);

          string instanceProxyId = instanceProxy.applicationId ?? instanceProxy.id;

          Guid id = doc.Objects.AddInstanceObject(index, transform, new ObjectAttributes() { LayerIndex = layerIndex });
          if (id == Guid.Empty)
          {
            conversionResults.Add(new(Status.ERROR, instanceProxy, instanceProxyId, "Instance (Block)"));
            continue;
          }

          applicationIdMap[instanceProxyId] = new List<string>() { id.ToString() };
          createdObjectIds.Add(id.ToString());
          conversionResults.Add(new(Status.SUCCESS, instanceProxy, id.ToString(), "Instance (Block)"));
        }
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        conversionResults.Add(new(Status.ERROR, instanceOrDefinition as Base ?? new Base(), null, null, ex));
      }
    }

    return new(createdObjectIds, consumedObjectIds, conversionResults);
  }

  public void PurgeInstances(string namePrefix)
  {
    var currentDoc = RhinoDoc.ActiveDoc; // POC: too much right now to interface around
    foreach (var definition in currentDoc.InstanceDefinitions)
    {
      if (!definition.IsDeleted && definition.Name.Contains(namePrefix))
      {
        currentDoc.InstanceDefinitions.Delete(definition.Index, true, false);
      }
    }
  }

  private Matrix4x4 XFormToMatrix(Transform t) =>
    new(t.M00, t.M01, t.M02, t.M03, t.M10, t.M11, t.M12, t.M13, t.M20, t.M21, t.M22, t.M23, t.M30, t.M31, t.M32, t.M33);

  private Transform MatrixToTransform(Matrix4x4 matrix, string units)
  {
    var currentDoc = RhinoDoc.ActiveDoc; // POC: too much right now to interface around
    var conversionFactor = Units.GetConversionFactor(units, currentDoc.ModelUnitSystem.ToSpeckleString());

    var t = Transform.Identity;
    t.M00 = matrix.M11;
    t.M01 = matrix.M12;
    t.M02 = matrix.M13;
    t.M03 = matrix.M14 * conversionFactor;

    t.M10 = matrix.M21;
    t.M11 = matrix.M22;
    t.M12 = matrix.M23;
    t.M13 = matrix.M24 * conversionFactor;

    t.M20 = matrix.M31;
    t.M21 = matrix.M32;
    t.M22 = matrix.M33;
    t.M23 = matrix.M34 * conversionFactor;

    t.M30 = matrix.M41;
    t.M31 = matrix.M42;
    t.M32 = matrix.M43;
    t.M33 = matrix.M44;
    return t;
  }
}
