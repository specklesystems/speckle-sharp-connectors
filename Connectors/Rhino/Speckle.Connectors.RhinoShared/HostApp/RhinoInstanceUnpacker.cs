using Microsoft.Extensions.Logging;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Speckle.Connectors.Common.Instances;
using Speckle.Connectors.Rhino.Extensions;
using Speckle.DoubleNumerics;
using Speckle.Sdk;
using Speckle.Sdk.Models.Instances;

namespace Speckle.Connectors.Rhino.HostApp;

public class RhinoInstanceUnpacker : IInstanceUnpacker<RhinoObject>
{
  private readonly IInstanceObjectsManager<RhinoObject, List<string>> _instanceObjectsManager;
  private readonly RhinoLayerHelper _rhinoLayerHelper;
  private readonly ILogger<RhinoInstanceUnpacker> _logger;

  public RhinoInstanceUnpacker(
    IInstanceObjectsManager<RhinoObject, List<string>> instanceObjectsManager,
    RhinoLayerHelper rhinoLayerHelper,
    ILogger<RhinoInstanceUnpacker> logger
  )
  {
    _instanceObjectsManager = instanceObjectsManager;
    _rhinoLayerHelper = rhinoLayerHelper;
    _logger = logger;
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
    try
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
          out List<InstanceProxy>? instanceProxiesWithSameDefinition
        )
      )
      {
        instanceProxiesWithSameDefinition = new List<InstanceProxy>();
        _instanceObjectsManager.AddInstanceProxiesByDefinitionId(
          instanceDefinitionId,
          instanceProxiesWithSameDefinition
        );
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

      if (
        _instanceObjectsManager.TryGetInstanceDefinitionProxy(instanceDefinitionId, out InstanceDefinitionProxy? value)
      )
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

      // NOTE: InstanceDefinition.GetObjects() returns all constituent objects of a block, but those constituent
      // objects can be on layers, that are not visible. The publish should respect that.
      // See request: [CNX-2254](https://linear.app/speckle/issue/CNX-2254/rhino-publish-blocks-with-hidden-objects)
      var allDefinitionObjects = instance.InstanceDefinition.GetObjects();
      var visibleDefinitionObjects = _rhinoLayerHelper.FilterByLayerVisibility(allDefinitionObjects);
      foreach (var obj in visibleDefinitionObjects)
      {
        definition.objects.Add(obj.Id.ToString());
        if (obj is InstanceObject localInstance)
        {
          UnpackInstance(localInstance, depth + 1);
        }

        _instanceObjectsManager.AddAtomicObject(obj.Id.ToString(), obj);
      }
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, "Failed unpacking Rhino instance");
    }
  }

  private Matrix4x4 XFormToMatrix(Transform t) =>
    new(t.M00, t.M01, t.M02, t.M03, t.M10, t.M11, t.M12, t.M13, t.M20, t.M21, t.M22, t.M23, t.M30, t.M31, t.M32, t.M33);
}
