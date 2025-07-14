using Rhino.Geometry;
using Speckle.Connectors.Common.Operations.Receive;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.GraphTraversal;
using Speckle.Sdk.Models.Instances;

namespace Speckle.Connectors.GrasshopperShared.Operations.Receive;

/// <summary>
/// Reconstructs block instances and definitions from received proxies back into Grasshopper wrapper objects.
/// Tracks and returns object IDs consumed by block definitions to prevent duplication in collection hierarchy.
/// </summary>
/// <remarks>
/// Geometry objects that define blocks must already be converted and present in convertedObjectsMap at this stage.
/// Follows Rhino's pattern where objects consumed by block definitions should not appear as standalone objects.
/// </remarks>
internal sealed class GrasshopperBlockUnpacker
{
  private readonly TraversalContextUnpacker _traversalContextUnpacker;
  private readonly GrasshopperColorUnpacker _colorUnpacker;
  private readonly GrasshopperMaterialUnpacker _materialUnpacker;

  public GrasshopperBlockUnpacker(
    TraversalContextUnpacker traversalContextUnpacker,
    GrasshopperColorUnpacker colorUnpacker,
    GrasshopperMaterialUnpacker materialUnpacker
  )
  {
    _traversalContextUnpacker = traversalContextUnpacker;
    _colorUnpacker = colorUnpacker;
    _materialUnpacker = materialUnpacker;
  }

  /// <summary>
  /// Creates block definitions and instances from receive pipeline, returning consumed object IDs.
  /// </summary>
  /// <returns>Set of object IDs that have been consumed by block definitions and should not appear standalone</returns>
  public HashSet<string> UnpackBlocks(
    IReadOnlyCollection<TraversalContext> blockComponents,
    IReadOnlyCollection<InstanceDefinitionProxy>? definitionProxies,
    Dictionary<string, SpeckleGeometryWrapper> convertedObjectsMap,
    GrasshopperCollectionRebuilder collectionRebuilder
  )
  {
    var consumedObjectIds = new HashSet<string>();
    var sortedComponents = ExtractAndSortBlocks(blockComponents, definitionProxies);

    CreateBlocksInDependencyOrder(sortedComponents, convertedObjectsMap, collectionRebuilder, consumedObjectIds);

    return consumedObjectIds;
  }

  /// <summary>
  /// Extracts blocks from TraversalContext and adds metadata definitions, then sorts by depth.
  /// Deepest definitions first, then instances, to handle nested hierarchies correctly.
  /// </summary>
  private List<(Collection[] path, IInstanceComponent component)> ExtractAndSortBlocks(
    IReadOnlyCollection<TraversalContext> blockComponents,
    IReadOnlyCollection<InstanceDefinitionProxy>? definitionProxies
  )
  {
    var allComponents = new List<(Collection[] path, IInstanceComponent component)>();

    // Extract instances from traversal contexts
    foreach (var traversalContext in blockComponents)
    {
      if (traversalContext.Current is IInstanceComponent instanceComponent)
      {
        var collectionPath = _traversalContextUnpacker.GetCollectionPath(traversalContext).ToArray();
        allComponents.Add((collectionPath, instanceComponent));
      }
    }

    // Add definition proxies from metadata (these don't have collection paths)
    if (definitionProxies != null)
    {
      foreach (var definitionProxy in definitionProxies)
      {
        allComponents.Add((Array.Empty<Collection>(), definitionProxy));
      }
    }

    // Sort by depth (deepest first) then by type (definitions before instances)
    return allComponents
      .OrderByDescending(x => x.component.maxDepth)
      .ThenBy(x => x.component is InstanceDefinitionProxy ? 0 : 1)
      .ToList();
  }

  /// <summary>
  /// Creates definitions and instances in dependency order, populating convertedObjectsMap
  /// with instances as they're created (following Rhino's applicationIdMap pattern).
  /// </summary>
  private void CreateBlocksInDependencyOrder(
    List<(Collection[] path, IInstanceComponent component)> sortedComponents,
    Dictionary<string, SpeckleGeometryWrapper> convertedObjectsMap,
    GrasshopperCollectionRebuilder collectionRebuilder,
    HashSet<string> consumedObjectIds
  )
  {
    var definitions = new Dictionary<string, SpeckleBlockDefinitionWrapper>();

    // NOTE: This relies on ExtractAndSortBlocks to have done its job correctly!
    foreach (var (collectionPath, component) in sortedComponents)
    {
      if (component is InstanceDefinitionProxy definitionProxy)
      {
        // Create definition using current state of convertedObjectsMap
        var definitionId = definitionProxy.applicationId ?? definitionProxy.id ?? Guid.NewGuid().ToString();
        var definition = CreateBlockDefinitionWrapper(
          definitionProxy,
          definitionId,
          convertedObjectsMap,
          consumedObjectIds
        );
        if (definition != null)
        {
          definitions[definitionId] = definition;
        }
        else
        {
          // TODO: throw?
        }
      }
      else if (component is InstanceProxy instanceProxy)
      {
        // Create instance using available definitions
        string instanceId = instanceProxy.applicationId ?? instanceProxy.id ?? Guid.NewGuid().ToString();
        SpeckleBlockInstanceWrapper? instance = CreateBlockInstanceWrapper(
          instanceProxy,
          instanceId,
          definitions,
          _colorUnpacker,
          _materialUnpacker
        );
        if (instance != null)
        {
          AddInstanceToCollection(instance, collectionPath, collectionRebuilder);
          convertedObjectsMap[instanceId] = instance;
        }
        else
        {
          // TODO: throw?
        }
      }
    }
  }

  /// <summary>
  /// Creates a <see cref="SpeckleBlockDefinitionWrapper"/> from its proxy using pre-converted defining objects.
  /// Tracks consumed object IDs to prevent duplication in collection hierarchy.
  /// </summary>
  /// <remarks>
  /// Objects used in block definitions are considered "consumed" and should not appear as standalone objects,
  /// matching Rhino's behavior where doc.Objects.Delete() removes consumed objects after block creation.
  /// </remarks>
  private SpeckleBlockDefinitionWrapper? CreateBlockDefinitionWrapper(
    InstanceDefinitionProxy definitionProxy,
    string definitionId,
    Dictionary<string, SpeckleGeometryWrapper> convertedObjectsMap,
    HashSet<string> consumedObjectIds
  )
  {
    var definitionObjects = new List<SpeckleGeometryWrapper>();
    var currentDefinitionObjectIds = new HashSet<string>();

    foreach (var objectId in definitionProxy.objects)
    {
      if (convertedObjectsMap.TryGetValue(objectId, out var convertedObject))
      {
        definitionObjects.Add(convertedObject);
        currentDefinitionObjectIds.Add(objectId);
      }
      else
      {
        // TODO: throw?
      }
    }

    // Only create definition if we have objects
    if (definitionObjects.Count == 0)
    {
      return null;
    }

    // Track consumed objects (matches Rhino's consumedObjectIds.UnionWith pattern)
    consumedObjectIds.UnionWith(currentDefinitionObjectIds);

    return new SpeckleBlockDefinitionWrapper
    {
      Base = definitionProxy,
      Name = definitionProxy.name,
      Objects = definitionObjects,
      ApplicationId = definitionId
    };
  }

  /// <summary>
  /// Creates a <see cref="SpeckleBlockInstanceWrapper"/> from its proxy using.
  /// </summary>
  private SpeckleBlockInstanceWrapper? CreateBlockInstanceWrapper(
    InstanceProxy instanceProxy,
    string instanceId,
    Dictionary<string, SpeckleBlockDefinitionWrapper> definitions,
    GrasshopperColorUnpacker colorUnpacker,
    GrasshopperMaterialUnpacker materialUnpacker
  )
  {
    // Find the referenced definition
    if (!definitions.TryGetValue(instanceProxy.definitionId, out var definition))
    {
      return null; // Definition not found or failed to build
    }

    Transform transform = GrasshopperHelpers.MatrixToTransform(instanceProxy.transform, instanceProxy.units);
    return new SpeckleBlockInstanceWrapper
    {
      Base = instanceProxy,
      Name = instanceProxy["name"] as string ?? "",
      ApplicationId = instanceId,
      Transform = transform,
      Definition = definition,
      GeometryBase = new InstanceReferenceGeometry(Guid.Empty, transform), //Instances shouldn't be using this except for the filter objects node,
      Color = colorUnpacker.Cache.TryGetValue(instanceProxy.applicationId ?? "", out var cachedInstanceColor)
        ? cachedInstanceColor
        : null,
      Material = materialUnpacker.Cache.TryGetValue(instanceProxy.applicationId ?? "", out var cachedInstanceMaterial)
        ? cachedInstanceMaterial
        : null,
    };
  }

  /// <summary>
  /// Adds an instance to the collection and sets up hierarchy relationships.
  /// </summary>
  private void AddInstanceToCollection(
    SpeckleBlockInstanceWrapper instance,
    Collection[] collectionPath,
    GrasshopperCollectionRebuilder collectionRebuilder
  )
  {
    var pathList = collectionPath.ToList();

    // Get or create the target collection
    var targetCollection = collectionRebuilder.GetOrCreateSpeckleCollectionFromPath(
      pathList,
      _colorUnpacker,
      _materialUnpacker
    );

    // Set up instance hierarchy properties
    instance.Path = pathList.Select(c => c.name).ToList();
    instance.Parent = targetCollection;

    // Add to collection
    targetCollection.Elements.Add(instance);
  }
}
