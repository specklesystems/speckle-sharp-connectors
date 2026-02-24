using Speckle.Connectors.Common.Instances;
using Speckle.Connectors.Common.Operations.Receive;
using Speckle.Objects.Data;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.Instances;

namespace Speckle.Connectors.Revit.Operations.Receive;

public class FamilyUnpackStrategy : RevitUnpackStrategyBase
{
  private readonly ILocalToGlobalUnpacker _localToGlobalUnpacker;
  private readonly RootObjectUnpacker _rootObjectUnpacker;

  public FamilyUnpackStrategy(ILocalToGlobalUnpacker localToGlobalUnpacker, RootObjectUnpacker rootObjectUnpacker)
  {
    _localToGlobalUnpacker = localToGlobalUnpacker;
    _rootObjectUnpacker = rootObjectUnpacker;
  }

  public override UnpackStrategyResult Unpack(RootObjectUnpackerResult unpackedRoot)
  {
    var parentDataObjectMap = new Dictionary<string, DataObject>();
    var displayValueDefinitionIds = new HashSet<string>();

    // 1. Build parent maps and identify definitions used purely for DataObject display values
    PopulateParentDataObjectMap(unpackedRoot, parentDataObjectMap, displayValueDefinitionIds);

    // 2. Split out standard atomic objects from instance components
    var (atomicObjects, instanceComponents) = _rootObjectUnpacker.SplitAtomicObjectsAndInstances(
      unpackedRoot.ObjectsToConvert
    );

    // 3. Collect true definition geometries to filter out
    var consumedObjectIds = new HashSet<string>();
    if (unpackedRoot.DefinitionProxies != null)
    {
      foreach (var dp in unpackedRoot.DefinitionProxies)
      {
        var defId = dp.applicationId ?? dp.id.NotNull();
        if (!displayValueDefinitionIds.Contains(defId) && (dp.id == null || !displayValueDefinitionIds.Contains(dp.id)))
        {
          foreach (var objId in dp.objects)
          {
            consumedObjectIds.Add(objId);
          }
        }
      }
    }

    // 4. Filter out consumed objects
    var filteredAtomicObjects = atomicObjects
      .Where(tc =>
      {
        var appId = tc.Current.applicationId;
        var id = tc.Current.id;
        return (appId == null || !consumedObjectIds.Contains(appId)) && (id == null || !consumedObjectIds.Contains(id));
      })
      .ToList();

    // 5. Prepare true Family instances (ignore the display value proxies)
    var instanceComponentsWithPath = instanceComponents
      .Where(tc => tc.Current is not InstanceProxy proxy || !displayValueDefinitionIds.Contains(proxy.definitionId))
      .Select(tc => (Array.Empty<Collection>(), tc.Current as IInstanceComponent))
      .Where(x => x.Item2 != null)
      .Select(x => (x.Item1, x.Item2!))
      .ToList();

    // 6. Add true definition proxies
    if (unpackedRoot.DefinitionProxies != null)
    {
      var definitions = unpackedRoot
        .DefinitionProxies.Where(proxy =>
        {
          var defId = proxy.applicationId ?? proxy.id.NotNull();
          return !displayValueDefinitionIds.Contains(defId)
            && (proxy.id == null || !displayValueDefinitionIds.Contains(proxy.id));
        })
        .Select(proxy => (Array.Empty<Collection>(), proxy as IInstanceComponent));

      instanceComponentsWithPath.AddRange(definitions);
    }

    // 7. Flatten surviving atomic objects
    var localToGlobalMaps = _localToGlobalUnpacker.Unpack(unpackedRoot.DefinitionProxies, filteredAtomicObjects);

    // 8. Clean out DataObjects using the shared base logic!
    var cleanedMaps = FilterUnpackedDataObjects(localToGlobalMaps);

    return new UnpackStrategyResult(cleanedMaps, instanceComponentsWithPath, parentDataObjectMap);
  }
}
