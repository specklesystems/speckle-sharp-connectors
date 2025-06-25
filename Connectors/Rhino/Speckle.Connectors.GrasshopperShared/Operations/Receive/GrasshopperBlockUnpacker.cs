using Speckle.Connectors.Common.Operations.Receive;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.GraphTraversal;
using Speckle.Sdk.Models.Instances;

namespace Speckle.Connectors.GrasshopperShared.Operations.Receive;

/// <summary>
/// Reconstructs block instances and definitions from received proxies back into Grasshopper wrapper objects.
/// </summary>
/// <remarks>
/// Geometry objects that define blocks must already be converted and present in convertedObjectsMap at this stage.
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
  /// Processes block definitions and instances from receive pipeline and reconstructs them as Grasshopper wrapper objects.
  /// </summary>
  public void UnpackBlocks(
    IReadOnlyCollection<TraversalContext> blockComponents,
    IReadOnlyCollection<InstanceDefinitionProxy>? definitionProxies,
    Dictionary<string, SpeckleObjectWrapper> convertedObjectsMap,
    GrasshopperCollectionRebuilder collectionRebuilder
  )
  {
    // Step 1: Extract and sort all components by depth (deepest first)
    var sortedComponents = ExtractAndSortBlocks(blockComponents, definitionProxies);

    // Step 2: Process in dependency order - definitions and instances interleaved
    ProcessComponentsInDependencyOrder(sortedComponents, convertedObjectsMap, collectionRebuilder);
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
  /// Processes definitions and instances in dependency order, populating convertedObjectsMap
  /// with instances as they're created (following Rhino's applicationIdMap pattern).
  /// </summary>
  private void ProcessComponentsInDependencyOrder(
    List<(Collection[] path, IInstanceComponent component)> sortedComponents,
    Dictionary<string, SpeckleObjectWrapper> convertedObjectsMap,
    GrasshopperCollectionRebuilder collectionRebuilder
  )
  {
    var definitions = new Dictionary<string, SpeckleBlockDefinitionWrapper>();

    foreach (var (collectionPath, component) in sortedComponents)
    {
      if (component is InstanceDefinitionProxy definitionProxy)
      {
        // Build definition using current state of convertedObjectsMap
        var definition = BuildDefinition(definitionProxy, convertedObjectsMap);
        if (definition != null)
        {
          var definitionId = definitionProxy.applicationId ?? definitionProxy.id ?? Guid.NewGuid().ToString();
          definitions[definitionId] = definition;
        }
        else
        {
          // TODO: throw?
        }
      }
      else if (component is InstanceProxy instanceProxy)
      {
        // Build instance using available definitions
        var instance = BuildInstance(instanceProxy, definitions);
        if (instance != null)
        {
          PlaceInstanceInCollection(instance, collectionPath, collectionRebuilder);
          var instanceId = instanceProxy.applicationId ?? instanceProxy.id ?? Guid.NewGuid().ToString();
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
  /// Builds a single block definition from its proxy using pre-converted defining objects.
  /// /// </summary>
  private SpeckleBlockDefinitionWrapper? BuildDefinition(
    InstanceDefinitionProxy definitionProxy,
    Dictionary<string, SpeckleObjectWrapper> convertedObjectsMap
  )
  {
    var definitionObjects = new List<SpeckleObjectWrapper>();

    foreach (var objectId in definitionProxy.objects)
    {
      if (convertedObjectsMap.TryGetValue(objectId, out var convertedObject))
      {
        definitionObjects.Add(convertedObject);
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

    return new SpeckleBlockDefinitionWrapper
    {
      Base = definitionProxy,
      Name = definitionProxy.name,
      Objects = definitionObjects,
      ApplicationId = definitionProxy.applicationId ?? definitionProxy.id ?? Guid.NewGuid().ToString()
    };
  }

  /// <summary>
  /// Builds a single block instance from its proxy using pre-built definitions.
  /// </summary>
  private SpeckleBlockInstanceWrapper? BuildInstance(
    InstanceProxy instanceProxy,
    Dictionary<string, SpeckleBlockDefinitionWrapper> definitions
  )
  {
    // Find the referenced definition
    if (!definitions.TryGetValue(instanceProxy.definitionId, out var definition))
    {
      return null; // Definition not found or failed to build
    }

    return new SpeckleBlockInstanceWrapper
    {
      Base = instanceProxy,
      Name = $"Instance of {definition.Name}",
      ApplicationId = instanceProxy.applicationId ?? instanceProxy.id ?? Guid.NewGuid().ToString(),
      Definition = definition,
      GeometryBase = null // Block instances don't have direct geometry
    };
  }

  /// <summary>
  /// Places a block instance in the appropriate collection and sets up hierarchy relationships.
  /// </summary>
  private void PlaceInstanceInCollection(
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
