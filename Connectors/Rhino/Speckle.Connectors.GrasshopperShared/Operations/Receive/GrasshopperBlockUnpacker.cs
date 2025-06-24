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
/// Grasshopper equivalent of RhinoInstanceBaker, but instead of baking to a document, we reconstruct the block wrappers
/// and integrate them into the collection structure.
/// Block definitions reconstructed and stored by ID for instance reference.
/// Block instances reconstructed and then placed in collections by the caller.
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
    // Step 1: Extract and sort all blocks by depth
    var allComponents = ExtractAndSortBlocks(blockComponents, definitionProxies);

    // Step 2: Reconstruct all definitions first (instances will reference them)
    var reconstructedDefinitions = ReconstructAllDefinitions(allComponents, convertedObjectsMap);

    // Step 3: Reconstruct all instances and place them in collections
    ReconstructAndPlaceInstances(allComponents, reconstructedDefinitions, collectionRebuilder);
  }

  /// <summary>
  /// Extracts blocks from TraversalContext and adds metadata definitions, then sorts by depth.
  /// </summary>
  private List<(Collection[] path, IInstanceComponent component)> ExtractAndSortBlocks(
    IReadOnlyCollection<TraversalContext> blockComponents,
    IReadOnlyCollection<InstanceDefinitionProxy>? definitionProxies
  )
  {
    var allComponents = new List<(Collection[] path, IInstanceComponent component)>();

    // Extract from traversal contexts
    foreach (var traversalContext in blockComponents)
    {
      if (traversalContext.Current is IInstanceComponent instanceComponent)
      {
        var collectionPath = _traversalContextUnpacker.GetCollectionPath(traversalContext).ToArray();
        allComponents.Add((collectionPath, instanceComponent));
      }
    }

    // Add definition proxies from metadata
    if (definitionProxies != null)
    {
      foreach (var definitionProxy in definitionProxies)
      {
        allComponents.Add((Array.Empty<Collection>(), definitionProxy));
      }
    }

    // Sort by depth (deepest definitions first, then instances)
    return allComponents
      .OrderByDescending(x => x.component.maxDepth)
      .ThenBy(x => x.component is InstanceDefinitionProxy ? 0 : 1)
      .ToList();
  }

  /// <summary>
  /// Reconstructs all block definitions and returns them in a lookup map.
  /// Definitions are reconstructed independently of collection placement.
  /// </summary>
  private Dictionary<string, SpeckleBlockDefinitionWrapper> ReconstructAllDefinitions(
    List<(Collection[] path, IInstanceComponent component)> sortedComponents,
    Dictionary<string, SpeckleObjectWrapper> convertedObjectsMap
  )
  {
    var reconstructedDefinitions = new Dictionary<string, SpeckleBlockDefinitionWrapper>();

    foreach (var (_, component) in sortedComponents)
    {
      if (component is InstanceDefinitionProxy definitionProxy)
      {
        var definition = ReconstructBlockDefinition(definitionProxy, convertedObjectsMap);
        if (definition != null)
        {
          var definitionId = definitionProxy.applicationId ?? definitionProxy.id ?? Guid.NewGuid().ToString();
          reconstructedDefinitions[definitionId] = definition;
        }
      }
    }

    return reconstructedDefinitions;
  }

  /// <summary>
  /// Reconstructs all block instances and places them in the appropriate collections.
  /// </summary>
  private void ReconstructAndPlaceInstances(
    List<(Collection[] path, IInstanceComponent component)> sortedComponents,
    Dictionary<string, SpeckleBlockDefinitionWrapper> reconstructedDefinitions,
    GrasshopperCollectionRebuilder collectionRebuilder
  )
  {
    foreach (var (collectionPath, component) in sortedComponents)
    {
      if (component is InstanceProxy instanceProxy)
      {
        var instance = ReconstructBlockInstance(instanceProxy, reconstructedDefinitions);
        if (instance != null)
        {
          PlaceInstanceInCollection(instance, collectionPath, collectionRebuilder);
        }
      }
    }
  }

  /// <summary>
  /// Reconstructs a single block definition from its proxy.
  /// </summary>
  private SpeckleBlockDefinitionWrapper? ReconstructBlockDefinition(
    InstanceDefinitionProxy definitionProxy,
    Dictionary<string, SpeckleObjectWrapper> convertedObjectsMap
  )
  {
    try
    {
      // Find all constituent objects
      var definitionObjects = new List<SpeckleObjectWrapper>();

      foreach (var objectId in definitionProxy.objects)
      {
        if (convertedObjectsMap.TryGetValue(objectId, out var convertedObject))
        {
          definitionObjects.Add(convertedObject);
        }
        else
        {
          Console.WriteLine(
            $@"Warning: Could not find object with ID {objectId} for block definition {definitionProxy.name}"
          );
        }
      }

      if (definitionObjects.Count == 0)
      {
        Console.WriteLine($@"Warning: Block definition {definitionProxy.name} has no valid objects, skipping");
        return null;
      }

      // Create the wrapper
      return new SpeckleBlockDefinitionWrapper
      {
        Base = definitionProxy,
        Name = definitionProxy.name,
        Objects = definitionObjects,
        ApplicationId = definitionProxy.applicationId ?? definitionProxy.id ?? Guid.NewGuid().ToString()
      };
    }
    catch (ArgumentException ex)
    {
      Console.WriteLine(
        $@"Invalid arguments when reconstructing block definition {definitionProxy.name}: {ex.Message}"
      );
      return null;
    }
    catch (InvalidOperationException ex)
    {
      Console.WriteLine(
        $@"Invalid operation when reconstructing block definition {definitionProxy.name}: {ex.Message}"
      );
      return null;
    }
  }

  /// <summary>
  /// Reconstructs a single block instance from its proxy.
  /// </summary>
  private SpeckleBlockInstanceWrapper? ReconstructBlockInstance(
    InstanceProxy instanceProxy,
    Dictionary<string, SpeckleBlockDefinitionWrapper> reconstructedDefinitions
  )
  {
    try
    {
      // Find the referenced definition
      var definitionId = instanceProxy.definitionId;
      if (!reconstructedDefinitions.TryGetValue(definitionId, out var definition))
      {
        Console.WriteLine($@"Warning: Could not find definition with ID {definitionId} for block instance");
        return null;
      }

      // Create the wrapper
      return new SpeckleBlockInstanceWrapper
      {
        Base = instanceProxy,
        Name = $"Instance of {definition.Name}",
        ApplicationId = instanceProxy.applicationId ?? instanceProxy.id ?? Guid.NewGuid().ToString(),
        Definition = definition,
        GeometryBase = null
      };
    }
    catch (ArgumentException ex)
    {
      Console.WriteLine($@"Invalid arguments when reconstructing block instance: {ex.Message}");
      return null;
    }
    catch (InvalidOperationException ex)
    {
      Console.WriteLine($@"Invalid operation when reconstructing block instance: {ex.Message}");
      return null;
    }
  }

  /// <summary>
  /// Places a reconstructed block instance in the appropriate collection.
  /// </summary>
  private void PlaceInstanceInCollection(
    SpeckleBlockInstanceWrapper instance,
    Collection[] collectionPath,
    GrasshopperCollectionRebuilder collectionRebuilder
  )
  {
    // Convert to the format expected by collection rebuilder
    var pathList = collectionPath.ToList();

    // Get or create the target collection
    var targetCollection = collectionRebuilder.GetOrCreateSpeckleCollectionFromPath(
      pathList,
      _colorUnpacker,
      _materialUnpacker
    );

    // Set up instance properties for collection placement
    instance.Path = pathList.Select(c => c.name).ToList();
    instance.Parent = targetCollection;

    // Add to collection
    targetCollection.Elements.Add(instance);
  }
}
