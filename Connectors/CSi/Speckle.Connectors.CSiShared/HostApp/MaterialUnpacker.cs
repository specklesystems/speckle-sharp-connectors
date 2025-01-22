using Speckle.Connectors.CSiShared.HostApp.Helpers;
using Speckle.Converters.CSiShared.ToSpeckle.Helpers;
using Speckle.Sdk.Models.Proxies;

namespace Speckle.Connectors.CSiShared.HostApp;

/// <summary>
/// Creates material proxies based on stored entries from the materials cache
/// </summary>
public class MaterialUnpacker
{
  private readonly CsiMaterialPropertyExtractor _propertyExtractor;
  private readonly CsiToSpeckleCacheSingleton _csiToSpeckleCacheSingleton;

  public MaterialUnpacker(
    CsiMaterialPropertyExtractor propertyExtractor,
    CsiToSpeckleCacheSingleton csiToSpeckleCacheSingleton
  )
  {
    _propertyExtractor = propertyExtractor;
    _csiToSpeckleCacheSingleton = csiToSpeckleCacheSingleton;
  }

  // Creates a list of material proxies from the csi materials cache
  public IEnumerable<IProxyCollection> UnpackMaterials()
  {
    foreach (var kvp in _csiToSpeckleCacheSingleton.MaterialCache)
    {
      // get the cached entry
      string materialName = kvp.Key;
      List<string> sectionIds = kvp.Value;

      // get the properties of the material
      Dictionary<string, object?> properties = new(); // create empty dictionary
      _propertyExtractor.ExtractProperties(materialName, properties); // dictionary mutated with respective properties

      // create the material proxy
      GroupProxy materialProxy =
        new()
        {
          id = materialName,
          name = materialName,
          applicationId = materialName,
          objects = sectionIds,
          ["properties"] = properties
        };

      yield return materialProxy;
    }
  }
}
