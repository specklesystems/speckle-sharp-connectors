using Autodesk.AutoCAD.DatabaseServices;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Autocad.HostApp.Extensions;
using Speckle.Connectors.Autocad.Operations.Send;
using Speckle.Connectors.Common.Instances;
using Speckle.Converters.AutocadShared.ToSpeckle;
using Speckle.Converters.Common;
using Speckle.DoubleNumerics;
using Speckle.Sdk;
using Speckle.Sdk.Models.Instances;

namespace Speckle.Connectors.Autocad.HostApp;

/// <summary>
/// Expects to be a scoped dependency per send operation.
/// </summary>
public class AutocadInstanceUnpacker : IInstanceUnpacker<AutocadRootObject>
{
  private readonly IHostToSpeckleUnitConverter<UnitsValue> _unitsConverter;
  private readonly IInstanceObjectsManager<AutocadRootObject, List<Entity>> _instanceObjectsManager;
  private readonly IPropertiesExtractor _propertiesExtractor;
  private readonly ILogger<AutocadInstanceUnpacker> _logger;

  public AutocadInstanceUnpacker(
    IHostToSpeckleUnitConverter<UnitsValue> unitsConverter,
    IInstanceObjectsManager<AutocadRootObject, List<Entity>> instanceObjectsManager,
    IPropertiesExtractor propertiesExtractor,
    ILogger<AutocadInstanceUnpacker> logger
  )
  {
    _unitsConverter = unitsConverter;
    _instanceObjectsManager = instanceObjectsManager;
    _propertiesExtractor = propertiesExtractor;
    _logger = logger;
  }

  public UnpackResult<AutocadRootObject> UnpackSelection(IEnumerable<AutocadRootObject> objects)
  {
    using var transaction = Application.DocumentManager.CurrentDocument.Database.TransactionManager.StartTransaction();

    foreach (var obj in objects)
    {
      // Note: isDynamicBlock always returns false for a selection of doc objects. Instances of dynamic blocks are represented in the document as blocks that have
      // a definition reference to the anonymous block table record.
      if (obj.Root is BlockReference blockReference && !blockReference.IsDynamicBlock)
      {
        UnpackInstance(blockReference, 0, transaction);
      }
      _instanceObjectsManager.AddAtomicObject(obj.ApplicationId, obj);
    }
    return _instanceObjectsManager.GetUnpackResult();
  }

  private void UnpackInstance(BlockReference instance, int depth, Transaction transaction)
  {
    try
    {
      string instanceId = instance.GetSpeckleApplicationId();

      // If this instance has a reference to an anonymous block, it means it's spawned from a dynamic block. Anonymous blocks are
      // used to represent specific "instances" of dynamic ones.
      // We do not want to send the full dynamic block definition, but its current "instance", as such here we're making sure we
      // take up the anon block table reference definition (if it exists). If it's not an instance of a dynamic block, we're
      // using the normal def reference.
      ObjectId definitionId = !instance.AnonymousBlockTableRecord.IsNull
        ? instance.AnonymousBlockTableRecord
        : instance.BlockTableRecord;

      InstanceProxy instanceProxy =
        new()
        {
          applicationId = instanceId,
          definitionId = definitionId.ToString(),
          maxDepth = depth,
          transform = GetMatrix(instance.BlockTransform.ToArray()),
          units = _unitsConverter.ConvertOrThrow(Application.DocumentManager.CurrentDocument.Database.Insunits)
        };

      var properties = _propertiesExtractor.GetProperties(instance);
      if (properties?.Count > 0)
      {
        instanceProxy["properties"] = properties;
      }

      _instanceObjectsManager.AddInstanceProxy(instanceId, instanceProxy);

      // For each block instance that has the same definition, we need to keep track of the "maximum depth" at which is found.
      // This will enable on receive to create them in the correct order (descending by max depth, interleaved definitions and instances).
      // We need to interleave the creation of definitions and instances, as some definitions may depend on instances.
      if (
        !_instanceObjectsManager.TryGetInstanceProxiesFromDefinitionId(
          definitionId.ToString(),
          out List<InstanceProxy>? instanceProxiesWithSameDefinition
        )
      )
      {
        instanceProxiesWithSameDefinition = new List<InstanceProxy>();
        _instanceObjectsManager.AddInstanceProxiesByDefinitionId(
          definitionId.ToString(),
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

      // Add text attributes from Instances as separate atomic objects:
      // AttributeReferences found on Instances are just a text, not a part of the Instance
      // They are not actually references and are not linked to AttributeDefinition (as one would expect),
      // and already have the correct position (no need for transforms).
      // We don't want to create a new BlockDefinition for every changed text for now, because AutoCAD API doesn't provide one,
      // e.g. AnonymousBlockTableRecord is provided for each dynamic blocks with geometry changes, but not for Attribute changes.
      // Docs on AttributeReference usage (used totally independent of AttributeDefinition): https://help.autodesk.com/view/OARX/2025/ENU/?guid=GUID-BA69D85A-2AED-43C2-B5B7-73022B5F28F8
      // Case of trying to match AttributeDefinition with AttributeReference via Tag value (which is not unique): https://forums.autodesk.com/t5/net-forum/get-the-value-of-an-attribute-in-c/td-p/9060940

      foreach (ObjectId id in instance.AttributeCollection)
      {
        var reference = (AttributeReference)transaction.GetObject(id, OpenMode.ForRead);
        string refAppId = reference.GetSpeckleApplicationId();
        _instanceObjectsManager.AddAtomicObject(refAppId, new(reference, refAppId));
      }

      // rely on already converted Definition
      if (
        _instanceObjectsManager.TryGetInstanceDefinitionProxy(
          definitionId.ToString(),
          out InstanceDefinitionProxy? value
        )
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

      var definition = (BlockTableRecord)transaction.GetObject(definitionId, OpenMode.ForRead);
      var definitionProxy = new InstanceDefinitionProxy()
      {
        applicationId = definitionId.ToString(),
        objects = new(),
        maxDepth = depth,
        name = !instance.AnonymousBlockTableRecord.IsNull ? "Dynamic instance " + definitionId : definition.Name
      };

      // Go through each definition object
      foreach (ObjectId id in definition)
      {
        Entity obj = (Entity)transaction.GetObject(id, OpenMode.ForRead);

        // In the case of dynamic blocks, this prevents sending objects that are not visible in its current state.
        // Also skipping AttributeDefinition because it only contains default text values. We convert AttributeReference above instead, as a separate object.
        if (!obj.Visible || obj is AttributeDefinition)
        {
          continue;
        }
        string appId = obj.GetSpeckleApplicationId();
        definitionProxy.objects.Add(appId);

        if (obj is BlockReference blockReference)
        {
          UnpackInstance(blockReference, depth + 1, transaction);
        }

        _instanceObjectsManager.AddAtomicObject(appId, new(obj, appId));
      }

      _instanceObjectsManager.AddDefinitionProxy(definitionId.ToString(), definitionProxy);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, "Failed unpacking Autocad instance");
    }
  }

  private Matrix4x4 GetMatrix(double[] t) =>
    new(t[0], t[1], t[2], t[3], t[4], t[5], t[6], t[7], t[8], t[9], t[10], t[11], t[12], t[13], t[14], t[15]);
}
