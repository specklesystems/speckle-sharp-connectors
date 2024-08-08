using Autodesk.AutoCAD.DatabaseServices;
using Objects.Other;
using Speckle.Connectors.Autocad.HostApp;
using Speckle.Connectors.Autocad.HostApp.Extensions;
using Speckle.Connectors.Utils.Builders;
using Speckle.Connectors.Utils.Conversion;
using Speckle.Connectors.Utils.Operations;
using Speckle.Converters.Common;
using Speckle.Core.Logging;
using Speckle.Core.Models;
using Speckle.Core.Models.Collections;
using Speckle.Core.Models.GraphTraversal;
using Speckle.Core.Models.Instances;
using Speckle.Core.Models.Proxies;
using AutocadColor = Autodesk.AutoCAD.Colors.Color;

namespace Speckle.Connectors.Autocad.Operations.Receive;

/// <summary>
/// <para>Expects to be a scoped dependency per receive operation.</para>
/// </summary>
public class AutocadHostObjectBuilder : IHostObjectBuilder
{
  private readonly AutocadLayerManager _autocadLayerManager;
  private readonly IRootToHostConverter _converter;
  private readonly GraphTraversal _traversalFunction;
  private readonly ISyncToThread _syncToThread;
  private readonly AutocadGroupManager _groupManager;
  private readonly AutocadMaterialManager _materialManager;
  private readonly AutocadColorManager _colorManager;
  private readonly AutocadInstanceObjectManager _instanceObjectsManager;
  private readonly AutocadContext _autocadContext;

  public AutocadHostObjectBuilder(
    IRootToHostConverter converter,
    GraphTraversal traversalFunction,
    AutocadLayerManager autocadLayerManager,
    AutocadGroupManager groupManager,
    AutocadInstanceObjectManager instanceObjectsManager,
    AutocadMaterialManager materialManager,
    AutocadColorManager colorManager,
    ISyncToThread syncToThread,
    AutocadContext autocadContext
  )
  {
    _converter = converter;
    _traversalFunction = traversalFunction;
    _autocadLayerManager = autocadLayerManager;
    _groupManager = groupManager;
    _instanceObjectsManager = instanceObjectsManager;
    _materialManager = materialManager;
    _colorManager = colorManager;
    _syncToThread = syncToThread;
    _autocadContext = autocadContext;
  }

  public Task<HostObjectBuilderResult> Build(
    Base rootObject,
    string projectName,
    string modelName,
    Action<string, double?>? onOperationProgressed,
    CancellationToken _
  )
  {
    return _syncToThread.RunOnThread(() =>
    {
      // Prompt the UI conversion started. Progress bar will swoosh.
      onOperationProgressed?.Invoke("Converting", null);

      // Layer filter for received commit with project and model name
      _autocadLayerManager.CreateLayerFilter(projectName, modelName);

      string baseLayerPrefix = _autocadContext.RemoveInvalidChars($"SPK-{projectName}-{modelName}-");

      PreReceiveDeepClean(baseLayerPrefix);

      List<ReceiveConversionResult> results = new();
      List<string> bakedObjectIds = new();

      var objectGraph = _traversalFunction.Traverse(rootObject).Where(obj => obj.Current is not Collection);

      // POC: these are not captured by traversal, so we need to re-add them here
      var instanceDefinitionProxies = (rootObject["instanceDefinitionProxies"] as List<object>)
        ?.Cast<InstanceDefinitionProxy>()
        .ToList();

      var instanceComponents = new List<(Collection[] path, IInstanceComponent obj)>();
      // POC: these are not captured by traversal, so we need to re-add them here
      if (instanceDefinitionProxies != null && instanceDefinitionProxies.Count > 0)
      {
        var transformed = instanceDefinitionProxies.Select(proxy =>
          (Array.Empty<Collection>(), proxy as IInstanceComponent)
        );
        instanceComponents.AddRange(transformed);
      }

      // POC: get colors
      List<ColorProxy>? colors = (rootObject["colorProxies"] as List<object>)?.Cast<ColorProxy>().ToList();
      if (colors != null)
      {
        _colorManager.ParseColors(colors, onOperationProgressed);
      }

      // POC: get render materials
      List<RenderMaterialProxy>? renderMaterials = (rootObject["renderMaterialProxies"] as List<object>)
        ?.Cast<RenderMaterialProxy>()
        .ToList();
      if (renderMaterials != null)
      {
        List<ReceiveConversionResult> materialResults = _materialManager.ParseAndBakeRenderMaterials(
          renderMaterials,
          baseLayerPrefix,
          onOperationProgressed
        );

        results.AddRange(materialResults);
      }

      // POC: get group proxies
      var groupProxies = (rootObject["groupProxies"] as List<object>)?.Cast<GroupProxy>().ToList();

      var atomicObjects = new List<(Collection[] layerPath, Base obj)>();

      foreach (TraversalContext tc in objectGraph)
      {
        // create new speckle layer from layer path
        Collection[] layerPath = _autocadLayerManager.GetLayerPath(tc);
        switch (tc.Current)
        {
          case IInstanceComponent instanceComponent:
            instanceComponents.Add((layerPath, instanceComponent));
            break;
          case GroupProxy:
            continue;
          default:
            atomicObjects.Add((layerPath, tc.Current));
            break;
        }
      }

      // Stage 1: Convert atomic objects
      Dictionary<string, List<Entity>> applicationIdMap = new();
      var count = 0;
      foreach (var (layerPath, atomicObject) in atomicObjects)
      {
        string objectId = atomicObject.applicationId ?? atomicObject.id;
        onOperationProgressed?.Invoke("Converting objects", (double)++count / atomicObjects.Count);
        try
        {
          List<Entity> convertedObjects = ConvertObject(atomicObject, layerPath, baseLayerPrefix).ToList();

          applicationIdMap[objectId] = convertedObjects;

          results.AddRange(
            convertedObjects.Select(e => new ReceiveConversionResult(
              Status.SUCCESS,
              atomicObject,
              e.Handle.Value.ToString(),
              e.GetType().ToString()
            ))
          );

          bakedObjectIds.AddRange(convertedObjects.Select(e => e.Handle.Value.ToString()));
        }
        catch (Exception ex) when (!ex.IsFatal())
        {
          results.Add(new(Status.ERROR, atomicObject, null, null, ex));
        }
      }

      // Stage 2: Convert instances
      var (createdInstanceIds, consumedObjectIds, instanceConversionResults) = _instanceObjectsManager.BakeInstances(
        instanceComponents,
        applicationIdMap,
        baseLayerPrefix,
        onOperationProgressed
      );

      bakedObjectIds.RemoveAll(id => consumedObjectIds.Contains(id));
      bakedObjectIds.AddRange(createdInstanceIds);
      results.RemoveAll(result => result.ResultId != null && consumedObjectIds.Contains(result.ResultId));
      results.AddRange(instanceConversionResults);

      // Stage 3: Create group
      if (groupProxies != null)
      {
        List<ReceiveConversionResult> groupResults = _groupManager.CreateGroups(groupProxies, applicationIdMap);
        results.AddRange(groupResults);
      }

      return new HostObjectBuilderResult(bakedObjectIds, results);
    });
  }

  private void PreReceiveDeepClean(string baseLayerPrefix)
  {
    _autocadLayerManager.DeleteAllLayersByPrefix(baseLayerPrefix);
    _instanceObjectsManager.PurgeInstances(baseLayerPrefix);
    _materialManager.PurgeMaterials(baseLayerPrefix);
  }

  private IEnumerable<Entity> ConvertObject(Base obj, Collection[] layerPath, string baseLayerNamePrefix)
  {
    string layerName = _autocadLayerManager.CreateLayerForReceive(
      layerPath,
      baseLayerNamePrefix,
      _colorManager.ObjectColorsIdMap,
      _materialManager.ObjectMaterialsIdMap
    );

    object converted;
    using (var tr = Application.DocumentManager.CurrentDocument.Database.TransactionManager.StartTransaction())
    {
      converted = _converter.Convert(obj);

      IEnumerable<Entity?> flattened = Utilities.FlattenToHostConversionResult(converted).Cast<Entity>();

      // get color and material if any
      string objId = obj.applicationId ?? obj.id;
      AutocadColor? objColor = _colorManager.ObjectColorsIdMap.TryGetValue(objId, out AutocadColor? color)
        ? color
        : null;
      ObjectId objMaterial = _materialManager.ObjectMaterialsIdMap.TryGetValue(objId, out ObjectId matId)
        ? matId
        : ObjectId.Null;

      foreach (Entity? conversionResult in flattened)
      {
        if (conversionResult == null)
        {
          // POC: This needed to be double checked why we check null and continue
          continue;
        }

        // set color and material
        // POC: if these are displayvalue meshes, we will need to check for their ids somehow
        if (objColor is not null)
        {
          conversionResult.Color = objColor;
        }

        if (objMaterial != ObjectId.Null)
        {
          conversionResult.MaterialId = objMaterial;
        }

        conversionResult.AppendToDb(layerName);
        yield return conversionResult;
      }

      tr.Commit();
    }
  }
}
