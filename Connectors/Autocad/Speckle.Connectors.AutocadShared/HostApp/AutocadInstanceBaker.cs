using System.Diagnostics.CodeAnalysis;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Autocad.HostApp.Extensions;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Instances;
using Speckle.Connectors.Common.Operations;
using Speckle.Converters.Autocad;
using Speckle.Converters.Common;
using Speckle.DoubleNumerics;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Dependencies;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.Instances;
using Speckle.Sdk.Pipelines.Progress;
using AutocadColor = Autodesk.AutoCAD.Colors.Color;

namespace Speckle.Connectors.Autocad.HostApp;

/// <summary>
/// Expects to be a scoped dependency receive operation.
/// </summary>
public class AutocadInstanceBaker : IInstanceBaker<IReadOnlyCollection<Entity>>
{
  private readonly AutocadLayerBaker _layerBaker;
  private readonly IAutocadColorBaker _colorBaker;
  private readonly IAutocadMaterialBaker _materialBaker;
  private readonly AutocadContext _autocadContext;
  private readonly ILogger<AutocadInstanceBaker> _logger;
  private readonly IConverterSettingsStore<AutocadConversionSettings> _converterSettings;

  public AutocadInstanceBaker(
    AutocadLayerBaker layerBaker,
    IAutocadColorBaker colorBaker,
    IAutocadMaterialBaker materialBaker,
    AutocadContext autocadContext,
    ILogger<AutocadInstanceBaker> logger,
    IConverterSettingsStore<AutocadConversionSettings> converterSettings
  )
  {
    _layerBaker = layerBaker;
    _colorBaker = colorBaker;
    _materialBaker = materialBaker;
    _autocadContext = autocadContext;
    _logger = logger;
    _converterSettings = converterSettings;
  }

  [SuppressMessage("Maintainability", "CA1506:Avoid excessive class coupling")]
  public BakeResult BakeInstances(
    ICollection<(Collection[] collectionPath, IInstanceComponent obj)> instanceComponents,
    Dictionary<string, IReadOnlyCollection<Entity>> applicationIdMap,
    string baseLayerName,
    IProgress<CardProgress> onOperationProgressed
  )
  {
    var sortedInstanceComponents = instanceComponents
      .OrderByDescending(x => x.obj.maxDepth) // Sort by max depth, so we start baking from the deepest element first
      .ThenBy(x => x.obj is InstanceDefinitionProxy ? 0 : 1) // Ensure we bake the deepest definition first, then any instances that depend on it
      .ToList();

    var definitionIdAndApplicationIdMap = new Dictionary<string, ObjectId>();

    using var transaction = Application.DocumentManager.CurrentDocument.Database.TransactionManager.StartTransaction();
    var conversionResults = new HashSet<ReceiveConversionResult>();
    var createdObjectIds = new HashSet<string>();
    var consumedObjectIds = new HashSet<string>();
    var count = 0;

    foreach (var (collectionPath, instanceOrDefinition) in sortedInstanceComponents)
    {
      try
      {
        onOperationProgressed.Report(new("Converting blocks", (double)++count / sortedInstanceComponents.Count));

        if (instanceOrDefinition is InstanceDefinitionProxy { applicationId: not null } definitionProxy)
        {
          // TODO: create definition (block table record)
          var constituentEntities = definitionProxy
            .objects.Select(id =>
              applicationIdMap.TryGetValue(id, out IReadOnlyCollection<Entity>? value) ? value : null
            )
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
          consumedObjectIds.UnionWith(consumedEntitiesHandleValues);
          createdObjectIds.RemoveWhere(newId => consumedEntitiesHandleValues.Contains(newId));
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
          string layerName = _layerBaker.CreateLayerForReceive(collectionPath, baseLayerName);

          // get color and material if any
          string instanceId = instanceProxy.applicationId ?? instanceProxy.id.NotNull();
          AutocadColor? objColor = _colorBaker.ObjectColorsIdMap.TryGetValue(instanceId, out AutocadColor? color)
            ? color
            : null;
          ObjectId objMaterial = _materialBaker.ObjectMaterialsIdMap.TryGetValue(instanceId, out ObjectId matId)
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
        _logger.LogError(ex, "Failed to create an instance from proxy");
        conversionResults.Add(new(Status.ERROR, instanceOrDefinition as Base ?? new Base(), null, null, ex));
      }
    }

    transaction.Commit();
    return new(createdObjectIds.Freeze(), consumedObjectIds.Freeze(), conversionResults.Freeze());
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

  private Matrix3d GetMatrix3d(Matrix4x4 matrix, string units)
  {
    var sf = Units.GetConversionFactor(units, _converterSettings.Current.SpeckleUnits);

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
  private double[] MakePerpendicular(Matrix3d matrix)
  {
    // Get the basis vectors of the matrix
    Vector3d right = new(matrix[0, 0], matrix[1, 0], matrix[2, 0]);
    Vector3d up = new(matrix[0, 1], matrix[1, 1], matrix[2, 1]);

    Vector3d newForward = right.CrossProduct(up).GetNormal();
    Vector3d newUp = newForward.CrossProduct(right).GetNormal();

    return
    [
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
      matrix[3, 3]
    ];
  }
}
