using Autodesk.AutoCAD.DatabaseServices;
using Speckle.Connectors.Autocad.HostApp;
using Speckle.Connectors.Autocad.HostApp.Extensions;
using Speckle.Connectors.Utils.Builders;
using Speckle.Connectors.Utils.Conversion;
using Speckle.Converters.Common;
using Speckle.Core.Logging;
using Speckle.Core.Models;
using Speckle.Core.Models.GraphTraversal;
using Speckle.Core.Models.Instances;

namespace Speckle.Connectors.Autocad.Operations.Receive;

/// <summary>
/// <para>Expects to be a scoped dependency per receive operation.</para>
/// </summary>
public class AutocadHostObjectBuilder : IHostObjectBuilder
{
  private readonly AutocadLayerManager _autocadLayerManager;
  private readonly IRootToHostConverter _converter;
  private readonly GraphTraversal _traversalFunction;

  // private readonly HashSet<string> _uniqueLayerNames = new();
  private readonly AutocadInstanceObjectManager _instanceObjectsManager;

  public AutocadHostObjectBuilder(
    IRootToHostConverter converter,
    GraphTraversal traversalFunction,
    AutocadLayerManager autocadLayerManager,
    AutocadInstanceObjectManager instanceObjectsManager
  )
  {
    _converter = converter;
    _traversalFunction = traversalFunction;
    _autocadLayerManager = autocadLayerManager;
    _instanceObjectsManager = instanceObjectsManager;
  }

  public HostObjectBuilderResult Build(
    Base rootObject,
    string projectName,
    string modelName,
    Action<string, double?>? onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    // Prompt the UI conversion started. Progress bar will swoosh.
    onOperationProgressed?.Invoke("Converting", null);

    // Layer filter for received commit with project and model name
    _autocadLayerManager.CreateLayerFilter(projectName, modelName);

    //TODO: make the layerManager handle \/ ?
    string baseLayerPrefix = $"SPK-{projectName}-{modelName}-";

    PreReceiveDeepClean(baseLayerPrefix);

    List<ReceiveConversionResult> results = new();
    List<string> bakedObjectIds = new();

    // return new(bakedObjectIds, results);

    var objectGraph = _traversalFunction.Traverse(rootObject).Where(obj => obj.Current is not Collection);

    // POC: these are not captured by traversal, so we need to re-add them here
    var instanceDefinitionProxies = (rootObject["instanceDefinitionProxies"] as List<object>)
      ?.Cast<InstanceDefinitionProxy>()
      .ToList();

    var instanceComponents = new List<(string[] path, IInstanceComponent obj)>();
    // POC: these are not captured by traversal, so we need to re-add them here
    if (instanceDefinitionProxies != null && instanceDefinitionProxies.Count > 0)
    {
      var transformed = instanceDefinitionProxies.Select(proxy => (Array.Empty<string>(), proxy as IInstanceComponent));
      instanceComponents.AddRange(transformed);
    }

    var atomicObjects = new List<(Collection collection, Base obj)>();

    foreach (TraversalContext tc in objectGraph)
    {
      string layerName = _autocadLayerManager.GetLayerPath(tc, baseLayerPrefix, out Collection? lastLayerCollection);
      Collection layerCollection = lastLayerCollection ?? new();
      layerCollection.name = layerName;

      if (tc.Current is IInstanceComponent instanceComponent)
      {
        instanceComponents.Add((new string[] { layerName }, instanceComponent));
      }
      else
      {
        atomicObjects.Add((layerCollection, tc.Current));
      }
    }

    // Stage 1: Convert atomic objects
    Dictionary<string, List<Entity>> applicationIdMap = new();
    var count = 0;
    foreach (var (layerCollection, atomicObject) in atomicObjects)
    {
      onOperationProgressed?.Invoke("Converting objects", (double)++count / atomicObjects.Count);
      try
      {
        var convertedObjects = ConvertObject(atomicObject, layerCollection).ToList();

        if (atomicObject.applicationId != null)
        {
          applicationIdMap[atomicObject.applicationId] = convertedObjects;
        }

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

    return new(bakedObjectIds, results);
  }

  private void PreReceiveDeepClean(string baseLayerPrefix)
  {
    _autocadLayerManager.DeleteAllLayersByPrefix(baseLayerPrefix);
    _instanceObjectsManager.PurgeInstances(baseLayerPrefix);
  }

  private IEnumerable<Entity> ConvertObject(Base obj, Collection layerCollection)
  {
    using TransactionContext transactionContext = TransactionContext.StartTransaction(
      Application.DocumentManager.MdiActiveDocument
    );

    _autocadLayerManager.CreateLayerForReceive(layerCollection);

    object converted;
    using (var tr = Application.DocumentManager.CurrentDocument.Database.TransactionManager.StartTransaction())
    {
      converted = _converter.Convert(obj);
      tr.Commit();
    }

    IEnumerable<Entity?> flattened = Utilities.FlattenToHostConversionResult(converted).Cast<Entity>();

    foreach (Entity? conversionResult in flattened)
    {
      if (conversionResult == null)
      {
        // POC: This needed to be double checked why we check null and continue
        continue;
      }

      conversionResult.AppendToDb(layerCollection.name);
      yield return conversionResult;
    }
  }
}
