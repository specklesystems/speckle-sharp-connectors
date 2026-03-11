using Speckle.Connectors.Common.Instances;
using Speckle.Connectors.Common.Operations.Receive;
using Speckle.Objects.Data;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models.Instances;

namespace Speckle.Connectors.Revit.Operations.Receive;

public abstract class RevitUnpackStrategyBase : IRevitUnpackStrategy
{
  public abstract UnpackStrategyResult Unpack(RootObjectUnpackerResult unpackedRoot);

  /// <summary>
  /// Builds a map of definition IDs and geometry IDs to their parent DataObject to preserve metadata.
  /// </summary>
  protected void PopulateParentDataObjectMap(
    RootObjectUnpackerResult unpackedRoot,
    Dictionary<string, DataObject> map,
    HashSet<string>? displayValueDefinitionIds = null
  )
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
            displayValueDefinitionIds?.Add(ip.definitionId);
          }
        }
      }
    }

    if (unpackedRoot.DefinitionProxies is not null)
    {
      foreach (var defProxy in unpackedRoot.DefinitionProxies)
      {
        var defId = defProxy.applicationId ?? defProxy.id.NotNull();
        if (
          definitionToDataObject.TryGetValue(defId, out var parentDataObject)
          || (defProxy.id != null && definitionToDataObject.TryGetValue(defProxy.id, out parentDataObject))
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

  /// <summary>
  /// Removes DataObjects that use InstanceProxies as display values from the map list.
  /// Their geometries are already flattened, and this prevents the geometry converter from crashing.
  /// </summary>
  protected IReadOnlyCollection<LocalToGlobalMap> FilterUnpackedDataObjects(
    IReadOnlyCollection<LocalToGlobalMap> maps
  ) =>
    maps.Where(map =>
      {
        if (map.AtomicObject is DataObject dataObject && dataObject.displayValue.Any(dv => dv is InstanceProxy))
        {
          return false;
        }
        return true;
      })
      .ToList();
}
