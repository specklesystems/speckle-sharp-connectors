using Speckle.Connectors.CSiShared.HostApp.Helpers;
using Speckle.Converters.CSiShared.ToSpeckle.Helpers;
using Speckle.Sdk.Models.Proxies;

namespace Speckle.Connectors.ETABSShared.HostApp;

public class EtabsMaterialUnpacker : IMaterialUnpacker
{
  private readonly CsiToSpeckleCacheSingleton _csiToSpeckleCacheSingleton;
  private readonly IMaterialPropertyExtractor _csiMaterialPropertyExtractor;
  private readonly IApplicationMaterialPropertyExtractor _etabsMaterialPropertyExtractor;

  public EtabsMaterialUnpacker(
    CsiToSpeckleCacheSingleton csiToSpeckleCacheSingleton,
    IMaterialPropertyExtractor csiMaterialPropertyExtractor,
    IApplicationMaterialPropertyExtractor etabsMaterialPropertyExtractor
  )
  {
    _csiToSpeckleCacheSingleton = csiToSpeckleCacheSingleton;
    _csiMaterialPropertyExtractor = csiMaterialPropertyExtractor;
    _etabsMaterialPropertyExtractor = etabsMaterialPropertyExtractor;
  }

  public IEnumerable<IProxyCollection> UnpackMaterials()
  {
    foreach (var kvp in _csiToSpeckleCacheSingleton.MaterialCache)
    {
      string name = kvp.Key;
      var sectionIds = kvp.Value;

      // get the properties of the material
      Dictionary<string, object?> properties = [];
      _csiMaterialPropertyExtractor.ExtractProperties(name, properties);
      _etabsMaterialPropertyExtractor.ExtractProperties(name, properties);

      // create the material proxy
      GroupProxy materialProxy =
        new()
        {
          id = name,
          name = name,
          applicationId = name,
          objects = sectionIds,
          ["properties"] = properties,
        };

      yield return materialProxy;
    }
  }
}
