using Grasshopper.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Instances;
using Speckle.Connectors.Common.Operations.Receive;
using Speckle.Connectors.Grasshopper8.Components.BaseComponents;
using Speckle.Connectors.Grasshopper8.Parameters;
using Speckle.Sdk;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.Instances;

namespace Speckle.Connectors.Grasshopper8.Components.Collections;

public record UnpackRootObjectComponentInput(Base RootObject) { }

public record UnpackRootObjectComponentOutput(
  List<Base> Elements,
  List<string> ElementPaths,
  List<IInstanceComponent> Instances,
  List<string> InstancePaths
) { }

public class UnpackRootObjectComponent
  : SpeckleScopedTaskCapableComponent<UnpackRootObjectComponentInput, UnpackRootObjectComponentOutput>
{
  public UnpackRootObjectComponent()
    : base("Unpack Root Object", "SURO", "Unpacks the root object from a receive operation", "Speckle", "Collections")
  { }

  public override Guid ComponentGuid => new Guid("3C770686-20D5-434C-99E3-BDE735E8267F");

  protected override void RegisterInputParams(GH_InputParamManager pManager)
  {
    pManager.AddParameter(new SpeckleObjectParam(GH_ParamAccess.item));
  }

  protected override void RegisterOutputParams(GH_OutputParamManager pManager)
  {
    pManager.AddTextParameter("Element Paths", "EP", "Path to the element in the collection tree", GH_ParamAccess.list);
    pManager.AddParameter(new SpeckleObjectParam(), "Elements", "E", "Elements", GH_ParamAccess.list);
    pManager.AddTextParameter(
      "Instance Paths",
      "IP",
      "Path to the instance in the collection tree",
      GH_ParamAccess.list
    );
    pManager.AddParameter(new SpeckleObjectParam(), "Instances", "I", "Instances", GH_ParamAccess.list);
  }

  protected override UnpackRootObjectComponentInput GetInput(IGH_DataAccess da)
  {
    Base? baseObject = null;
    da.GetData(0, ref baseObject);
    if (baseObject == null)
    {
      throw new SpeckleException("No base object provided");
    }
    return new UnpackRootObjectComponentInput(baseObject);
  }

  protected override void SetOutput(IGH_DataAccess da, UnpackRootObjectComponentOutput result)
  {
    da.SetDataList(0, result.ElementPaths);
    da.SetDataList(1, result.Elements);
    da.SetDataList(2, result.InstancePaths);
    da.SetDataList(3, result.Instances);
  }

  protected override async Task<UnpackRootObjectComponentOutput> PerformScopedTask(
    UnpackRootObjectComponentInput input,
    IServiceScope scope,
    CancellationToken cancellationToken = default
  )
  {
    var rootObjectUnpacker = scope.ServiceProvider.GetRequiredService<RootObjectUnpacker>();
    var contextUnpacker = scope.ServiceProvider.GetRequiredService<TraversalContextUnpacker>();

    var unpackedRoot = rootObjectUnpacker.Unpack(input.RootObject);

    // 2 - Split atomic objects and instance components with their path
    var (atomicObjects, instanceComponents) = rootObjectUnpacker.SplitAtomicObjectsAndInstances(
      unpackedRoot.ObjectsToConvert
    );

    var atomicObjectsWithPath = contextUnpacker.GetAtomicObjectsWithPath(atomicObjects);
    var instanceComponentsWithPath = contextUnpacker.GetInstanceComponentsWithPath(instanceComponents);

    // 2.1 - these are not captured by traversal, so we need to re-add them here
    if (unpackedRoot.DefinitionProxies != null && unpackedRoot.DefinitionProxies.Count > 0)
    {
      var transformed = unpackedRoot.DefinitionProxies.Select(proxy =>
        (Array.Empty<Collection>(), proxy as IInstanceComponent)
      );
      instanceComponentsWithPath.AddRange(transformed);
    }

    var applicationIdMap = new Dictionary<string, Base>();
    atomicObjectsWithPath.ForEach(a => applicationIdMap.Add(a.current.applicationId ?? a.current.id, a.current));

    var instanceResult = await ProcessInstances(instanceComponentsWithPath, applicationIdMap).ConfigureAwait(false);

    foreach (string objId in instanceResult.ConsumedObjectIds)
    {
      var indexAtomic = atomicObjectsWithPath.FindIndex(o => o.current.id == objId);
      if (indexAtomic != -1)
      {
        atomicObjectsWithPath.RemoveAt(indexAtomic);
      }
      // HACK: Why is instancecomponent not ISpeckleObject?
      var indexInstance = instanceComponentsWithPath.FindIndex(o => ((Base)o.instance).id == objId);
      if (indexInstance != -1)
      {
        instanceComponentsWithPath.RemoveAt(indexInstance);
      }
    }

    var elements = new List<Base>();
    var instances = new List<IInstanceComponent>();
    var elementPaths = new List<string>();
    var instancePaths = new List<string>();

    atomicObjectsWithPath.ForEach(atomicObj =>
    {
      var names = atomicObj.path.Select(p => p.name);
      elements.Add(atomicObj.current);
      elementPaths.Add(string.Join("::", names));
    });

    instanceComponentsWithPath.ForEach(instanceObj =>
    {
      var names = instanceObj.path.Select(p => p.name);
      instances.Add(instanceObj.instance);
      instancePaths.Add(string.Join("::", names));
    });

    var output = new UnpackRootObjectComponentOutput(elements, elementPaths, instances, instancePaths);

    return output;
  }

  public Task<BakeResult> ProcessInstances(
    IReadOnlyCollection<(Collection[] collectionPath, IInstanceComponent obj)> instanceComponents,
    Dictionary<string, Base> applicationIdMap
  )
  {
    var sortedInstanceComponents = instanceComponents
      .OrderByDescending(x => x.obj.maxDepth) // Sort by max depth, so we start baking from the deepest element first
      .ThenBy(x => x.obj is InstanceDefinitionProxy ? 0 : 1) // Ensure we bake the deepest definition first, then any instances that depend on it
      .ToList();

    var definitionObjectsMap = new Dictionary<string, (InstanceDefinitionProxy, List<Base>)>();

    var consumedObjectIds = new List<string>();
    foreach (var (layerCollection, instanceOrDefinition) in sortedInstanceComponents)
    {
      try
      {
        if (instanceOrDefinition is InstanceDefinitionProxy definitionProxy)
        {
          var currentSpeckleObjects = definitionProxy
            .objects.Where(applicationIdMap.ContainsKey)
            .Select(x => applicationIdMap[x])
            .ToList();

          definitionObjectsMap.Add(
            definitionProxy.applicationId ?? definitionProxy.id,
            (definitionProxy, currentSpeckleObjects)
          );

          consumedObjectIds.AddRange(currentSpeckleObjects.Select(o => o.id));
          consumedObjectIds.Add(definitionProxy.id);
        }

        if (
          instanceOrDefinition is InstanceProxy instanceProxy
          && definitionObjectsMap.TryGetValue(instanceProxy.definitionId, out var definition)
        )
        {
          instanceProxy["__geometry"] = definition.Item2;
          instanceProxy["__definition"] = definition.Item1;
          applicationIdMap[instanceProxy.applicationId ?? instanceProxy.id] = instanceProxy;
        }
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        //_logger.LogError(ex, "Failed to create an instance from proxy");
      }
    }

    //await Task.Yield();
    BakeResult processInstances = new(new List<string>(), consumedObjectIds, new List<ReceiveConversionResult>());
    return Task.FromResult(processInstances);
  }
}
