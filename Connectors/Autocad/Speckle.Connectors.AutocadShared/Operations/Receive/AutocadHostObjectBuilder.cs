using Autodesk.AutoCAD.DatabaseServices;
using Speckle.Connectors.Autocad.HostApp;
using Speckle.Connectors.Autocad.HostApp.Extensions;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Extensions;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Common.Operations.Receive;
using Speckle.Converters.Common;
using Speckle.Sdk;
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
public class AutocadHostObjectBuilder : IHostObjectBuilder
{
  private readonly AutocadLayerBaker _layerBaker;
  private readonly IRootToHostConverter _converter;
  private readonly ISyncToThread _syncToThread;
  private readonly AutocadGroupBaker _groupBaker;
  private readonly AutocadMaterialBaker _materialBaker;
  private readonly AutocadColorBaker _colorBaker;
  private readonly AutocadInstanceBaker _instanceBaker;
  private readonly AutocadContext _autocadContext;
  private readonly RootObjectUnpacker _rootObjectUnpacker;

  public AutocadHostObjectBuilder(
    IRootToHostConverter converter,
    AutocadLayerBaker layerBaker,
    AutocadGroupBaker groupBaker,
    AutocadInstanceBaker instanceBaker,
    AutocadMaterialBaker materialBaker,
    AutocadColorBaker colorBaker,
    ISyncToThread syncToThread,
    AutocadContext autocadContext,
    RootObjectUnpacker rootObjectUnpacker
  )
  {
    _converter = converter;
    _layerBaker = layerBaker;
    _groupBaker = groupBaker;
    _instanceBaker = instanceBaker;
    _materialBaker = materialBaker;
    _colorBaker = colorBaker;
    _syncToThread = syncToThread;
    _autocadContext = autocadContext;
    _rootObjectUnpacker = rootObjectUnpacker;
  }

  public async Task<HostObjectBuilderResult> Build(
    Base rootObject,
    string projectName,
    string modelName,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken _
  )
  {
    // NOTE: This is the only place we apply ISyncToThread across connectors. We need to sync up with main thread here
    //  after GetObject and Deserialization. It is anti-pattern now. Happiness level 3/10 but works.
    return await _syncToThread
      .RunOnThread(async () =>
      {
        await Task.CompletedTask.ConfigureAwait(true);
        return BuildSync(rootObject, projectName, modelName, onOperationProgressed);
      })
      .ConfigureAwait(false);
  }

  private HostObjectBuilderResult BuildSync(
    Base rootObject,
    string projectName,
    string modelName,
    IProgress<CardProgress> onOperationProgressed
  )
  {
    // Prompt the UI conversion started. Progress bar will swoosh.
    onOperationProgressed.Report(new("Converting", null));

    // Layer filter for received commit with project and model name
    _layerBaker.CreateLayerFilter(projectName, modelName);

    // 0 - Clean then Rock n Roll!
    string baseLayerPrefix = _autocadContext.RemoveInvalidChars($"SPK-{projectName}-{modelName}-");
    PreReceiveDeepClean(baseLayerPrefix);

    // 1 - Unpack objects and proxies from root commit object
    var unpackedRoot = _rootObjectUnpacker.Unpack(rootObject);

    // 2 - Split atomic objects and instance components with their path
    var (atomicObjects, instanceComponents) = _rootObjectUnpacker.SplitAtomicObjectsAndInstances(
      unpackedRoot.ObjectsToConvert
    );
    var atomicObjectsWithPath = _layerBaker.GetAtomicObjectsWithPath(atomicObjects);
    var instanceComponentsWithPath = _layerBaker.GetInstanceComponentsWithPath(instanceComponents);

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
      _materialBaker.ParseAndBakeRenderMaterials(
        unpackedRoot.RenderMaterialProxies,
        baseLayerPrefix,
        onOperationProgressed
      );
    }

    if (unpackedRoot.ColorProxies != null)
    {
      _colorBaker.ParseColors(unpackedRoot.ColorProxies, onOperationProgressed);
    }

    // 5 - Convert atomic objects
    HashSet<ReceiveConversionResult> results = new();
    HashSet<string> bakedObjectIds = new();
    Dictionary<string, IReadOnlyCollection<Entity>> applicationIdMap = new();
    var count = 0;
    foreach (var (layerPath, atomicObject) in atomicObjectsWithPath)
    {
      string objectId = atomicObject.applicationId ?? atomicObject.id.NotNull();
      onOperationProgressed.Report(new("Converting objects", (double)++count / atomicObjects.Count));
      try
      {
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
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        results.Add(new(Status.ERROR, atomicObject, null, null, ex));
      }
    }

    // 6 - Convert instances
    var (createdInstanceIds, consumedObjectIds, instanceConversionResults) = _instanceBaker.BakeInstances(
      instanceComponentsWithPath,
      applicationIdMap,
      baseLayerPrefix,
      onOperationProgressed
    );

    bakedObjectIds.RemoveWhere(id => consumedObjectIds.Contains(id));
    bakedObjectIds.UnionWith(createdInstanceIds);
    results.RemoveWhere(result => result.ResultId != null && consumedObjectIds.Contains(result.ResultId));
    results.UnionWith(instanceConversionResults);

    // 7 - Create groups
    if (unpackedRoot.GroupProxies != null)
    {
      IReadOnlyCollection<ReceiveConversionResult> groupResults = _groupBaker.CreateGroups(
        unpackedRoot.GroupProxies,
        applicationIdMap
      );
      results.UnionWith(groupResults);
    }

    return new HostObjectBuilderResult(bakedObjectIds, results);
  }

  private void PreReceiveDeepClean(string baseLayerPrefix)
  {
    _layerBaker.DeleteAllLayersByPrefix(baseLayerPrefix);
    _instanceBaker.PurgeInstances(baseLayerPrefix);
    _materialBaker.PurgeMaterials(baseLayerPrefix);
  }

  private IReadOnlyCollection<Entity> ConvertObject(Base obj, Collection[] layerPath, string baseLayerNamePrefix)
  {
    string layerName = _layerBaker.CreateLayerForReceive(layerPath, baseLayerNamePrefix);
    var convertedEntities = new HashSet<Entity>();

    using var tr = Application.DocumentManager.CurrentDocument.Database.TransactionManager.StartTransaction();

    // 1: convert
    var converted = _converter.Convert(obj);

    // 2: handle result
    if (converted is Entity entity)
    {
      var bakedEntity = BakeObject(entity, obj, layerName);
      convertedEntities.Add(bakedEntity);
    }
    else if (converted is List<(Entity, Base)> fallbackConversionResult)
    {
      var bakedFallbackEntities = BakeObjectsAsGroup(fallbackConversionResult, obj, layerName, baseLayerNamePrefix);
      convertedEntities.UnionWith(bakedFallbackEntities);
    }

    tr.Commit();
    return convertedEntities.Freeze();
  }

  private Entity BakeObject(Entity entity, Base originalObject, string layerName, Base? parentObject = null)
  {
    var objId = originalObject.applicationId ?? originalObject.id.NotNull();
    if (_colorBaker.ObjectColorsIdMap.TryGetValue(objId, out AutocadColor? color))
    {
      entity.Color = color;
    }

    if (_materialBaker.TryGetMaterialId(originalObject, parentObject, out ObjectId matId))
    {
      entity.MaterialId = matId;
    }

    entity.AppendToDb(layerName);
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

    var tr = Application.DocumentManager.CurrentDocument.Database.TransactionManager.TopTransaction;
    var groupDictionary = (DBDictionary)
      tr.GetObject(Application.DocumentManager.CurrentDocument.Database.GroupDictionaryId, OpenMode.ForWrite);

    var groupName = _autocadContext.RemoveInvalidChars(
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
