using Microsoft.Extensions.Logging;
using Rhino;
using Rhino.DocObjects;
using Rhino.FileIO;
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

  // ENG-9047: the Linux Rhino WIP native (librhcommon_c.so) ships none of the doc-level
  // instance C-API (CRhinoInstanceObject_*, CRhinoInstanceDefinition*, and the definition
  // table), so InstanceObject.InstanceDefinition throws EntryPointNotFoundException there.
  // Once observed, definitions are resolved from the doc's source 3dm via the opennurbs
  // surface (ON_InstanceRef/ON_InstanceDefinition/ONX_Model), which that native does
  // implement.
  private static bool s_docInstanceApiMissing;
  private Dictionary<Guid, InstanceDefinitionInfo>? _fileDefinitions;

  private sealed record InstanceDefinitionInfo(string Name, string Description, IReadOnlyList<RhinoObject> Objects);

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
      // Read the definition id and transform off the ON_InstanceRef geometry rather than
      // InstanceObject.InstanceDefinition/.InstanceXform: same underlying data, but the
      // geometry route also exists on the Linux WIP native (ENG-9047).
      var instanceGeometry = (InstanceReferenceGeometry)instance.Geometry;
      var definitionGuid = instanceGeometry.ParentIdefId;
      var instanceDefinitionId = definitionGuid.ToString();
      var currentDoc = RhinoDoc.ActiveDoc; // POC: too much right now to interface around

      InstanceProxy instanceProxy = new()
      {
        applicationId = instanceId,
        definitionId = instanceDefinitionId,
        transform = XFormToMatrix(instanceGeometry.Xform),
        maxDepth = depth,
        units = currentDoc.ModelUnitSystem.ToSpeckleString(),
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

      var definitionInfo = GetDefinition(instance, definitionGuid);

      var definition = new InstanceDefinitionProxy
      {
        applicationId = instanceDefinitionId,
        objects = new List<string>(),
        maxDepth = depth,
        name = definitionInfo.Name,
        ["description"] = definitionInfo.Description,
      };

      _instanceObjectsManager.AddDefinitionProxy(instanceDefinitionId, definition);

      // NOTE: InstanceDefinition.GetObjects() returns all constituent objects of a block, but those constituent
      // objects can be on layers, that are not visible. The publish should respect that.
      // See request: [CNX-2254](https://linear.app/speckle/issue/CNX-2254/rhino-publish-blocks-with-hidden-objects)
      var visibleDefinitionObjects = _rhinoLayerHelper.FilterByLayerVisibility(definitionInfo.Objects);
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

  private InstanceDefinitionInfo GetDefinition(InstanceObject instance, Guid definitionId)
  {
    if (!s_docInstanceApiMissing)
    {
      try
      {
        var definition = instance.InstanceDefinition;
        return new InstanceDefinitionInfo(definition.Name, definition.Description, definition.GetObjects());
      }
      catch (EntryPointNotFoundException)
      {
        s_docInstanceApiMissing = true;
      }
    }

    _fileDefinitions ??= ReadDefinitionsFromSourceFile(instance.Document);
    if (!_fileDefinitions.TryGetValue(definitionId, out var info))
    {
      throw new SpeckleException($"Instance definition {definitionId} not found in the doc's source 3dm");
    }
    return info;
  }

  private static Dictionary<Guid, InstanceDefinitionInfo> ReadDefinitionsFromSourceFile(RhinoDoc doc)
  {
    if (string.IsNullOrEmpty(doc.Path))
    {
      throw new SpeckleException(
        "Cannot resolve instance definitions: the doc-level instance API is unavailable on this platform and the doc has no source 3dm to read them from"
      );
    }

    // Known limit (ENG-9047): a linked (non-embedded) definition's file table entry carries
    // the link path, not member ids, so it resolves to an empty definition here — where the
    // doc-level GetObjects() would return the loaded linked geometry.
    using var file = File3dm.Read(doc.Path, File3dm.TableTypeFilter.InstanceDefinition, File3dm.ObjectTypeFilter.None);
    var definitions = new Dictionary<Guid, InstanceDefinitionInfo>();
    foreach (InstanceDefinitionGeometry definition in file.AllInstanceDefinitions)
    {
      var objects = new List<RhinoObject>();
      foreach (Guid objectId in definition.GetObjectIds())
      {
        // 3dm object ids survive the headless open, so the file's member list resolves
        // against the live doc; a miss (e.g. an id the doc dropped on open) is skipped the
        // same way the doc-level GetObjects() would simply not return it.
        if (doc.Objects.FindId(objectId) is RhinoObject obj)
        {
          objects.Add(obj);
        }
      }
      definitions[definition.Id] = new InstanceDefinitionInfo(definition.Name, definition.Description, objects);
    }
    return definitions;
  }

  private Matrix4x4 XFormToMatrix(Transform t) =>
    new(t.M00, t.M01, t.M02, t.M03, t.M10, t.M11, t.M12, t.M13, t.M20, t.M21, t.M22, t.M23, t.M30, t.M31, t.M32, t.M33);
}
