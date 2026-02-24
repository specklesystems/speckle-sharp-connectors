using Speckle.Connectors.Common.Instances;
using Speckle.Connectors.Common.Operations.Receive;
using Speckle.Objects.Data;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.Instances;

namespace Speckle.Connectors.Revit.Operations.Receive;

/// <summary>
/// The unpacking strategy used when receiving instances as native Revit Families.
/// </summary>
/// <remarks>
/// This strategy isolates instance components from standard "atomic" objects (standalone geometry).
/// It ensures that geometry consumed by definitions is filtered out so we don't accidentally
/// bake definition geometry as standalone DirectShapes in the main model.
/// </remarks>
public class FamilyUnpackStrategy : IRevitUnpackStrategy
{
  private readonly ILocalToGlobalUnpacker _localToGlobalUnpacker;
  private readonly RootObjectUnpacker _rootObjectUnpacker;

  public FamilyUnpackStrategy(ILocalToGlobalUnpacker localToGlobalUnpacker, RootObjectUnpacker rootObjectUnpacker)
  {
    _localToGlobalUnpacker = localToGlobalUnpacker;
    _rootObjectUnpacker = rootObjectUnpacker;
  }

  public UnpackStrategyResult Unpack(RootObjectUnpackerResult unpackedRoot)
  {
    // 1. Build a map of parent DataObjects (handles the InstanceProxy displayValue parent mapping)
    var parentDataObjectMap = new Dictionary<string, DataObject>();
    PopulateParentDataObjectMap(unpackedRoot, parentDataObjectMap);

    // 2. Split out standard atomic objects from instance components
    var (atomicObjects, instanceComponents) = _rootObjectUnpacker.SplitAtomicObjectsAndInstances(
      unpackedRoot.ObjectsToConvert
    );

    // 3. Collect all object IDs that are consumed by definitions (i.e. definition geometry)
    var consumedObjectIds =
      unpackedRoot.DefinitionProxies?.SelectMany(dp => dp.objects).ToHashSet() ?? new HashSet<string>();

    // 4. Filter out consumed objects from the atomic objects list
    // If we don't do this, definition geometry will appear as duplicate standalone DirectShapes in the model
    var filteredAtomicObjects = atomicObjects
      .Where(tc =>
      {
        var appId = tc.Current.applicationId;
        var id = tc.Current.id;
        return (appId == null || !consumedObjectIds.Contains(appId)) && (id == null || !consumedObjectIds.Contains(id));
      })
      .ToList();

    // 5. Prepare the instance components with empty paths
    var instanceComponentsWithPath = instanceComponents
      .Select(tc => (Array.Empty<Collection>(), tc.Current as IInstanceComponent))
      .Where(x => x.Item2 != null)
      .Select(x => (x.Item1, x.Item2!))
      .ToList();

    // 6. Add definition proxies (since these aren't captured by the standard graph traversal)
    if (unpackedRoot.DefinitionProxies != null)
    {
      var definitions = unpackedRoot.DefinitionProxies.Select(proxy =>
        (Array.Empty<Collection>(), proxy as IInstanceComponent)
      );
      instanceComponentsWithPath.AddRange(definitions);
    }

    // 7. Finally, pass the surviving standalone atomic objects through the pure local-to-global unpacker
    // to flatten their matrices for DirectShape conversion
    var localToGlobalMaps = _localToGlobalUnpacker.Unpack(null, filteredAtomicObjects);

    return new UnpackStrategyResult(localToGlobalMaps, instanceComponentsWithPath, parentDataObjectMap);
  }

  private void PopulateParentDataObjectMap(RootObjectUnpackerResult unpackedRoot, Dictionary<string, DataObject> map)
  {
    var definitionToDataObject = new Dictionary<string, DataObject>();

    foreach (var tc in unpackedRoot.ObjectsToConvert)
    {
      if (tc.Current is DataObject dataObject)
      {
        var instanceProxies = dataObject.displayValue.OfType<InstanceProxy>().ToList();
        if (instanceProxies.Count > 0)
        {
          foreach (var ip in instanceProxies)
          {
            definitionToDataObject[ip.definitionId] = dataObject;
          }
        }
      }
    }

    if (unpackedRoot.DefinitionProxies is not null)
    {
      foreach (var defProxy in unpackedRoot.DefinitionProxies)
      {
        if (
          defProxy.applicationId is not null
          && definitionToDataObject.TryGetValue(defProxy.applicationId, out var parentDataObject)
        )
        {
          foreach (var objectId in defProxy.objects)
          {
            map[objectId] = parentDataObject;
          }
        }
      }
    }
  }
}
