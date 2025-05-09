using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Autocad.HostApp.Extensions;
using Speckle.Connectors.Autocad.Operations.Send;
using Speckle.Connectors.Common.Instances;
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
  private readonly ILogger<AutocadInstanceUnpacker> _logger;

  public AutocadInstanceUnpacker(
    IHostToSpeckleUnitConverter<UnitsValue> unitsConverter,
    IInstanceObjectsManager<AutocadRootObject, List<Entity>> instanceObjectsManager,
    ILogger<AutocadInstanceUnpacker> logger
  )
  {
    _unitsConverter = unitsConverter;
    _instanceObjectsManager = instanceObjectsManager;
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

      // store all AttributeReferences in a dictionary with unique keys, so that the can be matched with AttributeDefinition later
      Dictionary<string, AttributeReference> attributeDict = GetAttributesDictionary(instance, transaction);

      InstanceProxy instanceProxy =
        new()
        {
          applicationId = instanceId,
          definitionId = definitionId.ToString(),
          maxDepth = depth,
          transform = GetMatrix(instance.BlockTransform.ToArray()),
          units = _unitsConverter.ConvertOrThrow(Application.DocumentManager.CurrentDocument.Database.Insunits)
        };
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

        // In the case of dynamic blocks, this prevents sending objects that are not visibile in its current state.
        if (!obj.Visible)
        {
          continue;
        }
        string appId = obj.GetSpeckleApplicationId();

        // In case of AttributeDefinition, get the AttributeReference of the current block instead, and convert outside of the block.
        // This will ensure the correct text string. Unlike for geometry, AutoCAD doesn't create an AnonymousBlockTableRecord for AttributeReferences
        if (
          obj is AttributeDefinition attrDefinition
          && attributeDict.TryGetValue(
            GetAttributeUniqueKey(attrDefinition, attrDefinition.Tag, instance.BlockTransform.ToArray()),
            out AttributeReference? reference
          )
        )
        {
          _instanceObjectsManager.AddAtomicObject(appId, new(reference, appId));
          continue;
        }

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

  private Dictionary<string, AttributeReference> GetAttributesDictionary(
    BlockReference instance,
    Transaction transaction
  )
  {
    Dictionary<string, AttributeReference> attributeDict = new();
    foreach (ObjectId id in instance.AttributeCollection)
    {
      // Generate a unique key for each AttributeReference, because AttributeReferences don't have any backwards connection to AttributeDefinition
      // The key is a combination of a unique Position, Justification and Tag. Just Tags can have the same names, therefore not sufficient.
      // https://forums.autodesk.com/t5/net-forum/know-which-attributedefinition-does-an-attributereference/td-p/6796502
      var attReference = (AttributeReference)transaction.GetObject(id, OpenMode.ForRead);
      attributeDict[GetAttributeUniqueKey(attReference, attReference.Tag, null)] = attReference;
    }

    return attributeDict;
  }

  private string GetAttributeUniqueKey(DBText attributeText, string tag, double[]? matrixArray)
  {
    var position = attributeText.Position;
    if (matrixArray is not null)
    {
      position = attributeText.Position.TransformBy(new Matrix3d(matrixArray));
    }

    Vector3 transformedVector = new(position.X, position.Y, position.Z);

    return $"{tag},{transformedVector},{attributeText.Justify}";
  }

  private Matrix4x4 GetMatrix(double[] t) =>
    new(t[0], t[1], t[2], t[3], t[4], t[5], t[6], t[7], t[8], t[9], t[10], t[11], t[12], t[13], t[14], t[15]);
}
