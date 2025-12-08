// using Microsoft.Extensions.Logging;
using Speckle.Converter.Navisworks.Constants;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Instances;

namespace Speckle.Converter.Navisworks.Services;

/// <summary>
/// Simple wrapper class that manages two SharedGeometryStores instances for dual instancing pattern.
/// Provides easy access to both mesh definitions store and instance definition proxies store.
/// </summary>
public class InstanceStoreManager(
// ILogger<InstanceStoreManager> logger
)
{
  // private readonly ILogger<InstanceStoreManager> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

  /// <summary>
  /// Store for geometry definitions (geometry data) - untransformed base geometries.
  /// </summary>
  internal SharedGeometryStore GeometryDefinitionsStore { get; } = new();

  /// <summary>
  /// Store for InstanceDefinitionProxy objects that reference geometry definitions.
  /// </summary>
  internal SharedGeometryStore InstanceDefinitionProxiesStore { get; } = new();

  /// <summary>
  /// Clears both stores for a new conversion session.
  /// Should be called at the start of each conversion.
  /// </summary>
  public void ClearAll()
  {
    GeometryDefinitionsStore.Clear();
    InstanceDefinitionProxiesStore.Clear();
  }

  /// <summary>
  /// Gets all instance definition proxies from the store, cast to their specific type.
  /// Useful for adding to root collection at end of conversion.
  /// </summary>
  public IReadOnlyCollection<InstanceDefinitionProxy> GetInstanceDefinitionProxies()
  {
    var proxies = InstanceDefinitionProxiesStore.Geometries.OfType<InstanceDefinitionProxy>().ToList().AsReadOnly();
    // _logger.LogDebug("GetInstanceDefinitionProxies returning {Count} proxies", proxies.Count);
    return proxies;
  }

  /// <summary>
  /// Gets all geometry definitions from the geometry definitions store.
  /// </summary>
  /// <returns></returns>
  public List<Base> GetGeometryDefinitions() => [.. GeometryDefinitionsStore.Geometries.ToList().AsReadOnly()];

  /// <summary>
  /// Gets a geometry definition by its application ID from the geometry definitions store.
  /// </summary>
  /// <returns>The geometry if found, null otherwise.</returns>
  public Base? GetGeometryDefinition(string fragmentId) =>
    GeometryDefinitionsStore.Geometries.FirstOrDefault(g =>
      g.applicationId == $"{InstanceConstants.GEOMETRY_ID_PREFIX}{fragmentId}"
    );

  /// <summary>
  /// Gets an instance definition proxy by its application ID.
  /// </summary>
  /// <returns>The instance definition proxy if found, null otherwise.</returns>
  public InstanceDefinitionProxy? GetInstanceDefinitionProxy(string fragmentId) =>
    InstanceDefinitionProxiesStore
      .Geometries.OfType<InstanceDefinitionProxy>()
      .FirstOrDefault(p => p.applicationId == $"{InstanceConstants.DEFINITION_ID_PREFIX}{fragmentId}");

  /// <summary>
  /// Adds geometry definitions and corresponding instance definition proxy for shared geometry.
  /// This is a convenience method that handles both stores in one call.
  /// Supports all geometry primitive types (Mesh, Lines, Points).
  /// </summary>
  /// <param name="fragmentId">The fragment-based application ID.</param>
  /// <param name="geometries">The untransformed base geometries (meshes, lines, points).</param>
  /// <returns>True if geometries were added (new geometry), false if they already existed.</returns>
  public bool AddSharedGeometry(string fragmentId, List<Base> geometries)
  {
    // _logger.LogDebug("AddSharedGeometry called for FragmentId={FragmentId}, GeometryCount={Count}", fragmentId, geometries.Count);

    if (geometries.Count == 0)
    {
      return false;
    }

    var geometriesAdded = false;
    var proxyAdded = false;

    // Create prefixed IDs using base fragment hash
    var definitionId = $"{InstanceConstants.DEFINITION_ID_PREFIX}{fragmentId}";
    var geometryApplicationIds = new List<string>();

    // _logger.LogDebug("Using DefinitionId={DefinitionId}", definitionId);

    // Add each geometry definition with a unique index suffix
    for (var i = 0; i < geometries.Count; i++)
    {
      var geometry = geometries[i];
      var geometryId =
        geometries.Count == 1
          ? $"{InstanceConstants.GEOMETRY_ID_PREFIX}{fragmentId}"
          : $"{InstanceConstants.GEOMETRY_ID_PREFIX}{fragmentId}_{i}";

      if (!GeometryDefinitionsStore.Contains(geometryId))
      {
        geometry.applicationId = geometryId;
        var added = GeometryDefinitionsStore.Add(geometry);
        geometriesAdded = geometriesAdded || added;
        // _logger.LogDebug("Added geometry definition: {GeometryId}, Type={Type}, Success={Success}", geometryId, geometry.GetType().Name, added);
      }
      else
      {
        // _logger.LogDebug("Geometry definition already exists: {GeometryId}", geometryId);
      }

      geometryApplicationIds.Add(geometryId);
    }

    // Add instance definition proxy if not exists
    if (!InstanceDefinitionProxiesStore.Contains(definitionId))
    {
      var definitionProxy = new InstanceDefinitionProxy
      {
        applicationId = definitionId,
        name = $"Shared Geometry {fragmentId[..8]}...", // Show first 8 chars for readability
        objects = geometryApplicationIds,
        maxDepth = 0
      };
      proxyAdded = InstanceDefinitionProxiesStore.Add(definitionProxy);
    }
    else
    {
      // _logger.LogDebug("Instance definition proxy already exists: {DefinitionId}", definitionId);
    }

    var conversionSucceededResult = geometriesAdded || proxyAdded;
    return conversionSucceededResult;
  }

  /// <summary>
  /// Checks if shared geometry already exists in the stores.
  /// Uses the instance definition proxy as the authoritative check since it references all geometries.
  /// </summary>
  /// <param name="fragmentId">The fragment-based application ID.</param>
  /// <returns>True if the instance definition proxy exists for this fragment.</returns>
  public bool ContainsSharedGeometry(string fragmentId) =>
    InstanceDefinitionProxiesStore.Contains($"{InstanceConstants.DEFINITION_ID_PREFIX}{fragmentId}");
}
