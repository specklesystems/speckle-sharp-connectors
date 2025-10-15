using Autodesk.AutoCAD.DatabaseServices;
using Speckle.Connectors.Autocad.HostApp;
using Speckle.Connectors.Autocad.HostApp.Extensions;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Extensions;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Common.Operations.Receive;
using Speckle.Converters.Civil3dShared.Helpers;
using Speckle.Converters.Common;
using Speckle.Sdk.Common;
using Speckle.Sdk.Dependencies;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.Instances;
using AutocadColor = Autodesk.AutoCAD.Colors.Color;

namespace Speckle.Connectors.Autocad.Operations.Receive;

/// <summary>
/// <para>Expects to be a scoped dependency per receive operation.</para>
/// </summary>
public class AutocadHostObjectBuilder(
  IRootToHostConverter converter,
  AutocadLayerBaker layerBaker,
  AutocadGroupBaker groupBaker,
  AutocadInstanceBaker instanceBaker,
  IAutocadMaterialBaker materialBaker,
  IAutocadColorBaker colorBaker,
  AutocadContext autocadContext,
  RootObjectUnpacker rootObjectUnpacker,
  IReceiveConversionHandler conversionHandler,
  PropertySetBaker? propertySetBaker = null
) : IHostObjectBuilder
{
  public Task<HostObjectBuilderResult> Build(
    Base rootObject,
    string projectName,
    string modelName,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    // Prompt the UI conversion started. Progress bar will swoosh.
    onOperationProgressed.Report(new("Converting", null));

    // Layer filter for received commit with project and model name
    layerBaker.CreateLayerFilter(projectName, modelName);

    // 0 - Clean then Rock n Roll!
    string baseLayerPrefix = autocadContext.RemoveInvalidChars($"SPK-{projectName}-{modelName}-");
    PreReceiveDeepClean(baseLayerPrefix);

    // 1 - Unpack objects and proxies from root commit object
    var unpackedRoot = rootObjectUnpacker.Unpack(rootObject);

    // 2 - Split atomic objects and instance components with their path
    var (atomicObjects, instanceComponents) = rootObjectUnpacker.SplitAtomicObjectsAndInstances(
      unpackedRoot.ObjectsToConvert
    );
    var atomicObjectsWithPath = layerBaker.GetAtomicObjectsWithPath(atomicObjects);
    var instanceComponentsWithPath = layerBaker.GetInstanceComponentsWithPath(instanceComponents);

    // POC: these are not captured by traversal, so we need to re-add them here
    if (unpackedRoot.DefinitionProxies != null && unpackedRoot.DefinitionProxies.Count > 0)
    {
      var transformed = unpackedRoot.DefinitionProxies.Select(proxy =>
        (Array.Empty<Collection>(), proxy as IInstanceComponent)
      );
      instanceComponentsWithPath.AddRange(transformed);
    }

    // 3 - Bake materials and colors, as they are used later down the line by layers and objects
    if (unpackedRoot.RenderMaterialProxies != null)
    {
      materialBaker.ParseAndBakeRenderMaterials(
        unpackedRoot.RenderMaterialProxies,
        baseLayerPrefix,
        onOperationProgressed
      );
    }

    if (unpackedRoot.ColorProxies != null)
    {
      colorBaker.ParseColors(unpackedRoot.ColorProxies, onOperationProgressed);
    }

    // 4 - Convert atomic objects
    HashSet<ReceiveConversionResult> results = new();
    HashSet<string> bakedObjectIds = new();
    Dictionary<string, IReadOnlyCollection<Entity>> applicationIdMap = new();
    var count = 0;
    foreach (var (layerPath, atomicObject) in atomicObjectsWithPath)
    {
      onOperationProgressed.Report(new("Converting objects", (double)++count / atomicObjects.Count));
      var ex = conversionHandler.TryConvert(() =>
      {
        cancellationToken.ThrowIfCancellationRequested();
        string objectId = atomicObject.applicationId ?? atomicObject.id.NotNull();
        IReadOnlyCollection<Entity> convertedObjects = ConvertObject(atomicObject, layerPath, baseLayerPrefix);

        applicationIdMap[objectId] = convertedObjects;

        results.UnionWith(
          convertedObjects.Select(e => new ReceiveConversionResult(
            Status.SUCCESS,
            atomicObject,
            e.GetSpeckleApplicationId(),
            e.GetType().ToString()
          ))
        );

        bakedObjectIds.UnionWith(convertedObjects.Select(e => e.GetSpeckleApplicationId()));
      });
      if (ex != null)
      {
        results.Add(new(Status.ERROR, atomicObject, null, null, ex));
      }
    }

    // 5 - Convert instances
    var (createdInstanceIds, consumedObjectIds, instanceConversionResults) = instanceBaker.BakeInstances(
      instanceComponentsWithPath,
      applicationIdMap,
      baseLayerPrefix,
      onOperationProgressed
    );

    bakedObjectIds.RemoveWhere(id => consumedObjectIds.Contains(id));
    bakedObjectIds.UnionWith(createdInstanceIds);
    results.RemoveWhere(result => result.ResultId != null && consumedObjectIds.Contains(result.ResultId));
    results.UnionWith(instanceConversionResults);

    // 6 - Create groups
    if (unpackedRoot.GroupProxies != null)
    {
      IReadOnlyCollection<ReceiveConversionResult> groupResults = groupBaker.CreateGroups(
        unpackedRoot.GroupProxies,
        applicationIdMap
      );
      results.UnionWith(groupResults);
    }

    return Task.FromResult(new HostObjectBuilderResult(bakedObjectIds, results));
  }

  private void PreReceiveDeepClean(string baseLayerPrefix)
  {
    layerBaker.DeleteAllLayersByPrefix(baseLayerPrefix);
    instanceBaker.PurgeInstances(baseLayerPrefix);
    materialBaker.PurgeMaterials(baseLayerPrefix);
  }

  private IReadOnlyCollection<Entity> ConvertObject(Base obj, Collection[] layerPath, string baseLayerNamePrefix)
  {
    string layerName = layerBaker.CreateLayerForReceive(layerPath, baseLayerNamePrefix);
    var convertedEntities = new HashSet<Entity>();

    using var tr = Application.DocumentManager.CurrentDocument.Database.TransactionManager.StartTransaction();

    // 1: convert
    var converted = converter.Convert(obj);

    // 2: handle result
    switch (converted)
    {
      case Entity entity:
        var bakedEntity = BakeObject(entity, obj, layerName);
        convertedEntities.Add(bakedEntity);
        break;

      case List<(Entity, Base)> listConversionResult: // this is from fallback conversion for brep/brepx/subdx/extrusionx/polycurve
        var bakedFallbackEntities = BakeObjectsAsGroup(listConversionResult, obj, layerName, baseLayerNamePrefix);
        convertedEntities.UnionWith(bakedFallbackEntities);
        break;

      default:
        // TODO: capture defualt case with report object here? Same as in Rhino
        break;
    }

    tr.Commit();
    return convertedEntities.Freeze();
  }

  private Entity BakeObject(Entity entity, Base originalObject, string layerName, Base? parentObject = null)
  {
    var objId = originalObject.applicationId ?? originalObject.id.NotNull();
    if (colorBaker.ObjectColorsIdMap.TryGetValue(objId, out AutocadColor? color))
    {
      entity.Color = color;
    }

    if (materialBaker.TryGetMaterialId(originalObject, parentObject, out ObjectId matId))
    {
      entity.MaterialId = matId;
    }

    entity.AppendToDb(layerName);

    propertySetBaker?.TryBakePropertySets(entity, originalObject);

    return entity;
  }

  private List<Entity> BakeObjectsAsGroup(
    List<(Entity, Base)> fallbackConversionResult,
    Base parentObject,
    string layerName,
    string baseLayerName
  )
  {
    var ids = new ObjectIdCollection();
    var entities = new List<Entity>();
    foreach (var (conversionResult, originalObject) in fallbackConversionResult)
    {
      BakeObject(conversionResult, originalObject, layerName, parentObject);
      ids.Add(conversionResult.ObjectId);
      entities.Add(conversionResult);
    }

    if (entities.Count <= 1) // return if empty list or only one, because we don't want to create empty or single item groups.
    {
      return entities;
    }

    var tr = Application.DocumentManager.CurrentDocument.Database.TransactionManager.TopTransaction;
    var groupDictionary = (DBDictionary)
      tr.GetObject(Application.DocumentManager.CurrentDocument.Database.GroupDictionaryId, OpenMode.ForWrite);

    var groupName = autocadContext.RemoveInvalidChars(
      $@"{parentObject.speckle_type.Split('.').Last()} - {parentObject.applicationId ?? parentObject.id}  ({baseLayerName})"
    );

    var newGroup = new Group(groupName, true);
    newGroup.Append(ids);
    groupDictionary.UpgradeOpen();
    groupDictionary.SetAt(groupName, newGroup);
    tr.AddNewlyCreatedDBObject(newGroup, true);

    return entities;
  }
}
