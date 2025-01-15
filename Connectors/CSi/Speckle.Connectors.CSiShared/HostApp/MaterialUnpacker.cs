using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.CSiShared.HostApp.Helpers;
using Speckle.Sdk;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models.Collections;
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
  private readonly ILogger<MaterialUnpacker> _logger;
  private readonly ICsiApplicationService _csiApplicationService;
  private readonly ISdkActivityFactory _activityFactory;
  private readonly CsiMaterialPropertyExtractor _propertyExtractor;

  public MaterialUnpacker(
    ILogger<MaterialUnpacker> logger,
    ICsiApplicationService csiApplicationService,
    ISdkActivityFactory activityFactory,
    CsiMaterialPropertyExtractor propertyExtractor
  )
  {
    _logger = logger;
    _csiApplicationService = csiApplicationService;
    _activityFactory = activityFactory;
    _propertyExtractor = propertyExtractor;
  }

  public List<IProxyCollection> UnpackMaterials(Collection rootObjectCollection)
  {
    try
    {
      using var activity = _activityFactory.Start("Unpack Materials");

      int numberOfMaterials = 0;
      string[] materialNames = [];
      _csiApplicationService.SapModel.PropMaterial.GetNameList(ref numberOfMaterials, ref materialNames);

      Dictionary<string, IProxyCollection> materials = [];

      foreach (string materialName in materialNames)
      {
        try
        {
          var properties = new Dictionary<string, object?>();
          _propertyExtractor.ExtractProperties(materialName, properties);

          GroupProxy materialProxy =
            new()
            {
              id = materialName,
              name = materialName,
              applicationId = materialName,
              objects = [],
              ["Properties"] = properties
            };

          materials[materialName] = materialProxy;
        }
        catch (Exception ex) when (!ex.IsFatal())
        {
          _logger.LogError(ex, "Failed to create material proxy for {MaterialName}", materialName);
        }
      }

      var materialProxies = materials.Values.ToList();
      if (materialProxies.Count > 0)
      {
        rootObjectCollection[ProxyKeys.MATERIAL] = materialProxies;
      }

      return materialProxies;
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, "Failed to unpack materials");
      return [];
    }
  }
}
