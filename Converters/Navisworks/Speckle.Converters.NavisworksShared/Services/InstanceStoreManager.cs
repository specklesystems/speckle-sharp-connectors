using Microsoft.Extensions.Logging;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Instances;

namespace Speckle.Converter.Navisworks.Services;

/// <summary>
/// Simple wrapper class that manages two SharedGeometryStores instances for dual instancing pattern.
/// Provides easy access to both mesh definitions store and instance definition proxies store.
/// </summary>
public class InstanceStoreManager(ILogger<InstanceStoreManager> logger)
{
  private readonly ILogger<InstanceStoreManager> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

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
    _logger.LogDebug("GetInstanceDefinitionProxies returning {Count} proxies", proxies.Count);
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
    GeometryDefinitionsStore.Geometries.FirstOrDefault(g => g.applicationId == $"geom_{fragmentId}");

  /// <summary>
  /// Gets an instance definition proxy by its application ID.
  /// </summary>
  /// <returns>The instance definition proxy if found, null otherwise.</returns>
  public InstanceDefinitionProxy? GetInstanceDefinitionProxy(string fragmentId) =>
    InstanceDefinitionProxiesStore
      .Geometries.OfType<InstanceDefinitionProxy>()
      .FirstOrDefault(p => p.applicationId == $"def_{fragmentId}");

  /// <summary>
  /// Adds a geometry definition and corresponding instance definition proxy for shared geometry.
  /// This is a convenience method that handles both stores in one call.
  /// </summary>
  /// <param name="fragmentId">The fragment-based application ID.</param>
  /// <param name="geometry">The untransformed base geometry.</param>
  /// <returns>True if both were added (new geometry), false if they already existed.</returns>
  public bool AddSharedGeometry(string fragmentId, Base geometry)
  {
    _logger.LogDebug("AddSharedGeometry called for FragmentId={FragmentId}", fragmentId);

    bool geometryAdded = false;
    bool proxyAdded = false;

    // Create prefixed IDs for 1:1:1 relationship using base fragment hash
    var geometryId = $"geom_{fragmentId}";
    var definitionId = $"def_{fragmentId}";

    _logger.LogDebug("Using GeometryId={GeometryId}, DefinitionId={DefinitionId}", geometryId, definitionId);

    // Add geometry definition if not exists
    if (!GeometryDefinitionsStore.Contains(geometryId))
    {
      geometry.applicationId = geometryId;
      geometryAdded = GeometryDefinitionsStore.Add(geometry);
      _logger.LogDebug("Added geometry definition: {GeometryId}, Success={Success}", geometryId, geometryAdded);
    }
    else
    {
      _logger.LogDebug("Geometry definition already exists: {GeometryId}", geometryId);
    }

    // Add instance definition proxy if not exists
    if (!InstanceDefinitionProxiesStore.Contains(definitionId))
    {
      if (geometry.applicationId == null)
      {
        _logger.LogWarning(
          "Cannot create instance definition proxy - geometry.id is null for FragmentId={FragmentId}",
          fragmentId
        );
        var result = geometryAdded || proxyAdded;
        _logger.LogDebug(
          "AddSharedGeometry completed: FragmentId={FragmentId}, Result={Result}, GeometryAdded={GeometryAdded}, ProxyAdded={ProxyAdded}",
          fragmentId,
          result,
          geometryAdded,
          proxyAdded
        );
        return result;
      }

      var definitionProxy = new InstanceDefinitionProxy
      {
        applicationId = definitionId,
        name = $"Shared Geometry {fragmentId[..8]}...", // Show first 8 chars for readability
        objects = [geometry.applicationId],
        maxDepth = 0
      };
      proxyAdded = InstanceDefinitionProxiesStore.Add(definitionProxy);
      _logger.LogDebug("Added instance definition proxy: {DefinitionId}, Success={Success}", definitionId, proxyAdded);
    }
    else
    {
      _logger.LogDebug("Instance definition proxy already exists: {DefinitionId}", definitionId);
    }

    var conversionSucceededResult = geometryAdded || proxyAdded;
    _logger.LogDebug(
      "AddSharedGeometry completed: FragmentId={FragmentId}, Result={Result}, GeometryAdded={GeometryAdded}, ProxyAdded={ProxyAdded}",
      fragmentId,
      conversionSucceededResult,
      geometryAdded,
      proxyAdded
    );
    return conversionSucceededResult;
  }

  /// <summary>
  /// Checks if shared geometry already exists in both stores.
  /// </summary>
  /// <param name="fragmentId">The fragment-based application ID.</param>
  /// <returns>True if geometry definition exists in both stores.</returns>
  public bool ContainsSharedGeometry(string fragmentId) => GeometryDefinitionsStore.Contains($"geom_{fragmentId}")
  // && InstanceDefinitionProxiesStore.Contains($"def_{fragmentId}")
  ;
}
