using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Speckle.Connectors.Autocad.HostApp.Extensions;
using Speckle.Connectors.Autocad.Operations.Send;
using Speckle.Connectors.Utils.Conversion;
using Speckle.Connectors.Utils.Instances;
using Speckle.Converters.Common;
using Speckle.DoubleNumerics;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.Instances;
using AutocadColor = Autodesk.AutoCAD.Colors.Color;

namespace Speckle.Connectors.Autocad.HostApp;

/// <summary>
///  Expects to be a scoped dependency per send or receive operation.
/// POC: Split later unpacker and baker.
/// </summary>
public class AutocadInstanceObjectManager : IInstanceUnpacker<AutocadRootObject>, IInstanceBaker<List<Entity>>
{
  private readonly AutocadLayerManager _autocadLayerManager;
  private readonly AutocadColorManager _autocadColorManager;
  private readonly AutocadMaterialManager _autocadMaterialManager;
  private readonly IHostToSpeckleUnitConverter<UnitsValue> _unitsConverter;
  private readonly AutocadContext _autocadContext;

  private readonly IInstanceObjectsManager<AutocadRootObject, List<Entity>> _instanceObjectsManager;

  public AutocadInstanceObjectManager(
    AutocadLayerManager autocadLayerManager,
    AutocadColorManager autocadColorManager,
    AutocadMaterialManager autocadMaterialManager,
    IHostToSpeckleUnitConverter<UnitsValue> unitsConverter,
    AutocadContext autocadContext,
    IInstanceObjectsManager<AutocadRootObject, List<Entity>> instanceObjectsManager
  )
  {
    _autocadLayerManager = autocadLayerManager;
    _autocadColorManager = autocadColorManager;
    _autocadMaterialManager = autocadMaterialManager;
    _unitsConverter = unitsConverter;
    _autocadContext = autocadContext;
    _instanceObjectsManager = instanceObjectsManager;
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
      _instanceObjectsManager.TryGetInstanceDefinitionProxy(definitionId.ToString(), out InstanceDefinitionProxy? value)
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
      name = !instance.AnonymousBlockTableRecord.IsNull ? "Dynamic instance " + definitionId : definition.Name,
      ["comments"] = definition.Comments
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
      definitionProxy.objects.Add(appId);

      if (obj is BlockReference blockReference)
      {
        UnpackInstance(blockReference, depth + 1, transaction);
      }

      _instanceObjectsManager.AddAtomicObject(appId, new(obj, appId));
    }

    _instanceObjectsManager.AddDefinitionProxy(definitionId.ToString(), definitionProxy);
  }

  public BakeResult BakeInstances(
    List<(Collection[] collectionPath, IInstanceComponent obj)> instanceComponents,
    Dictionary<string, List<Entity>> applicationIdMap,
    string baseLayerName,
    Action<string, double?>? onOperationProgressed
  )
  {
    var sortedInstanceComponents = instanceComponents
      .OrderByDescending(x => x.obj.maxDepth) // Sort by max depth, so we start baking from the deepest element first
      .ThenBy(x => x.obj is InstanceDefinitionProxy ? 0 : 1) // Ensure we bake the deepest definition first, then any instances that depend on it
      .ToList();

    var definitionIdAndApplicationIdMap = new Dictionary<string, ObjectId>();

    using var transaction = Application.DocumentManager.CurrentDocument.Database.TransactionManager.StartTransaction();
    var conversionResults = new List<ReceiveConversionResult>();
    var createdObjectIds = new List<string>();
    var consumedObjectIds = new List<string>();
    var count = 0;

    foreach (var (collectionPath, instanceOrDefinition) in sortedInstanceComponents)
    {
      try
      {
        onOperationProgressed?.Invoke("Converting blocks", (double)++count / sortedInstanceComponents.Count);
        if (instanceOrDefinition is InstanceDefinitionProxy { applicationId: not null } definitionProxy)
        {
          // TODO: create definition (block table record)
          var constituentEntities = definitionProxy
            .objects.Select(id => applicationIdMap.TryGetValue(id, out List<Entity>? value) ? value : null)
            .Where(x => x is not null)
            .SelectMany(ent => ent!)
            .ToList();

          var record = new BlockTableRecord();
          var objectIds = new ObjectIdCollection();
          // We're expecting to have Name prop always for definitions. If there is an edge case, ask to Dim or Ogu
          record.Name = _autocadContext.RemoveInvalidChars(
            $"{definitionProxy.name}-({definitionProxy.applicationId})-{baseLayerName}"
          );

          foreach (var entity in constituentEntities)
          {
            objectIds.Add(entity.ObjectId);
          }

          if (constituentEntities.Count == 0)
          {
            throw new ConversionException("No objects found to create instance definition.");
          }

          using var blockTable = (BlockTable)
            transaction.GetObject(Application.DocumentManager.CurrentDocument.Database.BlockTableId, OpenMode.ForWrite);
          var id = blockTable.Add(record);
          record.AssumeOwnershipOf(objectIds);

          definitionIdAndApplicationIdMap[definitionProxy.applicationId] = id;
          transaction.AddNewlyCreatedDBObject(record, true);
          var consumedEntitiesHandleValues = constituentEntities.Select(ent => ent.GetSpeckleApplicationId()).ToArray();
          consumedObjectIds.AddRange(consumedEntitiesHandleValues);
          createdObjectIds.RemoveAll(newId => consumedEntitiesHandleValues.Contains(newId));
        }
        else if (
          instanceOrDefinition is InstanceProxy instanceProxy
          && definitionIdAndApplicationIdMap.TryGetValue(instanceProxy.definitionId, out ObjectId definitionId)
        )
        {
          var matrix3d = GetMatrix3d(instanceProxy.transform, instanceProxy.units);
          var insertionPoint = Point3d.Origin.TransformBy(matrix3d);

          var modelSpaceBlockTableRecord = Application.DocumentManager.CurrentDocument.Database.GetModelSpace(
            OpenMode.ForWrite
          );

          // POC: collectionPath for instances should be an array of size 1, because we are flattening collections on traversal
          string layerName = _autocadLayerManager.CreateLayerForReceive(collectionPath, baseLayerName);

          // get color and material if any
          string instanceId = instanceProxy.applicationId ?? instanceProxy.id;
          AutocadColor? objColor = _autocadColorManager.ObjectColorsIdMap.TryGetValue(
            instanceId,
            out AutocadColor? color
          )
            ? color
            : null;
          ObjectId objMaterial = _autocadMaterialManager.ObjectMaterialsIdMap.TryGetValue(
            instanceId,
            out ObjectId matId
          )
            ? matId
            : ObjectId.Null;

          BlockReference blockRef = new(insertionPoint, definitionId) { BlockTransform = matrix3d, Layer = layerName, };

          if (objColor is not null)
          {
            blockRef.Color = objColor;
          }

          if (objMaterial != ObjectId.Null)
          {
            blockRef.MaterialId = objMaterial;
          }

          modelSpaceBlockTableRecord.AppendEntity(blockRef);

          applicationIdMap[instanceId] = new List<Entity> { blockRef };

          transaction.AddNewlyCreatedDBObject(blockRef, true);
          conversionResults.Add(
            new(Status.SUCCESS, instanceProxy, blockRef.GetSpeckleApplicationId(), "Instance (Block)")
          );
          createdObjectIds.Add(blockRef.GetSpeckleApplicationId());
        }
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        conversionResults.Add(new(Status.ERROR, instanceOrDefinition as Base ?? new Base(), null, null, ex));
      }
    }

    transaction.Commit();
    return new(createdObjectIds, consumedObjectIds, conversionResults);
  }

  /// <summary>
  /// Cleans up any previously created instances.
  /// POC: This function will not be able to delete block definitions if the user creates a new one composed out of received definitions.
  /// </summary>
  /// <param name="namePrefix"></param>
  public void PurgeInstances(string namePrefix)
  {
    namePrefix = _autocadContext.RemoveInvalidChars(namePrefix);
    using var transaction = Application.DocumentManager.CurrentDocument.Database.TransactionManager.StartTransaction();
    var instanceDefinitionsToDelete = new Dictionary<string, BlockTableRecord>();

    // Helper function that recurses through a given block table record's constituent objects and purges inner instances as required.
    void TraverseAndClean(BlockTableRecord btr)
    {
      foreach (var objectId in btr)
      {
        var obj = transaction.GetObject(objectId, OpenMode.ForRead) as BlockReference;
        if (obj == null)
        {
          continue;
        }
        var definition = (BlockTableRecord)transaction.GetObject(obj.BlockTableRecord, OpenMode.ForRead);
        if (obj.IsErased)
        {
          TraverseAndClean(definition);
          continue;
        }

        obj.UpgradeOpen();
        obj.Erase();
        TraverseAndClean(definition);
        instanceDefinitionsToDelete[obj.BlockTableRecord.ToString()] = definition;
      }
    }

    using var blockTable = (BlockTable)
      transaction.GetObject(Application.DocumentManager.CurrentDocument.Database.BlockTableId, OpenMode.ForRead);

    // deep clean definitions
    foreach (var btrId in blockTable)
    {
      var btr = (BlockTableRecord)transaction.GetObject(btrId, OpenMode.ForRead);
      if (btr.Name.Contains(namePrefix)) // POC: this is tightly coupled with a naming convention for definitions in the instance object manager
      {
        TraverseAndClean(btr);
        instanceDefinitionsToDelete[btr.Name] = btr;
      }
    }

    foreach (var def in instanceDefinitionsToDelete.Values)
    {
      def.UpgradeOpen();
      def.Erase();
    }

    transaction.Commit();
  }

  private Matrix4x4 GetMatrix(double[] t)
  {
    return new Matrix4x4(
      t[0],
      t[1],
      t[2],
      t[3],
      t[4],
      t[5],
      t[6],
      t[7],
      t[8],
      t[9],
      t[10],
      t[11],
      t[12],
      t[13],
      t[14],
      t[15]
    );
  }

  private Matrix3d GetMatrix3d(Matrix4x4 matrix, string units)
  {
    var sf = Units.GetConversionFactor(
      units,
      _unitsConverter.ConvertOrThrow(Application.DocumentManager.CurrentDocument.Database.Insunits)
    );

    var scaledTransform = new[]
    {
      matrix.M11,
      matrix.M12,
      matrix.M13,
      matrix.M14 * sf,
      matrix.M21,
      matrix.M22,
      matrix.M23,
      matrix.M24 * sf,
      matrix.M31,
      matrix.M32,
      matrix.M33,
      matrix.M34 * sf,
      matrix.M41,
      matrix.M42,
      matrix.M43,
      matrix.M44
    };

    var m3d = new Matrix3d(scaledTransform);
    if (!m3d.IsScaledOrtho())
    {
      m3d = new Matrix3d(MakePerpendicular(m3d));
    }

    return m3d;
  }

  // https://forums.autodesk.com/t5/net/set-blocktransform-values/m-p/6452121#M49479
  private static double[] MakePerpendicular(Matrix3d matrix)
  {
    // Get the basis vectors of the matrix
    Vector3d right = new(matrix[0, 0], matrix[1, 0], matrix[2, 0]);
    Vector3d up = new(matrix[0, 1], matrix[1, 1], matrix[2, 1]);

    Vector3d newForward = right.CrossProduct(up).GetNormal();
    Vector3d newUp = newForward.CrossProduct(right).GetNormal();

    return new[]
    {
      right.X,
      newUp.X,
      newForward.X,
      matrix[0, 3],
      right.Y,
      newUp.Y,
      newForward.Y,
      matrix[1, 3],
      right.Z,
      newUp.Z,
      newForward.Z,
      matrix[2, 3],
      0.0,
      0.0,
      0.0,
      matrix[3, 3],
    };
  }
}
