using Speckle.Connectors.CSiShared.HostApp.Helpers;
using Speckle.Converters.CSiShared.ToSpeckle.Helpers;
using Speckle.Sdk.Models.Proxies;

namespace Speckle.Connectors.CSiShared.HostApp;

/// <summary>
/// Extracts material proxies from the root object collection.
/// </summary>
/// <remarks>
/// Decouples material extraction from conversion processes. Supports complex material
/// property retrieval (dependent on material type) while maintaining a clean separation of concerns.
/// Enables extensible material proxy creation across different material types.
/// </remarks>
public class MaterialUnpacker
{
  // A cache storing a map of material name <-> section ids using this material
  public Dictionary<string, List<string>> MaterialCache { get; set; } = new();

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
    foreach (var entry in _csiToSpeckleCacheSingleton.MaterialCache)
    {
      string materialName = entry.Key;
      List<string> sectionIds = entry.Value;

      // get the properties of the material
      Dictionary<string, object?> properties = new();
      _propertyExtractor.ExtractProperties(materialName, properties);

      // create the material proxy
      GroupProxy materialProxy =
        new()
        {
          id = materialName,
          name = materialName,
          applicationId = materialName,
          objects = sectionIds,
          ["Properties"] = properties
        };

      yield return materialProxy;
    }
  }
}
