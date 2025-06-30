using Rhino.Geometry;
using Speckle.Connectors.Common.Operations.Receive;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Operations.Receive;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Sdk;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.GraphTraversal;
using Speckle.Sdk.Models.Instances;

/// <summary>
/// Handles conversion of atomic objects from TraversalContexts into Grasshopper wrapper objects.
/// </summary>
/// <remarks>
/// Follows Rhino's approach: atomic objects are converted directly without pre-transformation,
/// with instance transformations handled separately during block reconstruction. Implements consumedObjectIds
/// tracking to prevent objects consumed by block definitions from appearing as standalone objects.
/// </remarks>
internal sealed class LocalToGlobalMapHandler
{
  public Dictionary<string, SpeckleObjectWrapper> ConvertedObjectsMap { get; } = new();
  public readonly GrasshopperCollectionRebuilder CollectionRebuilder;

  private readonly TraversalContextUnpacker _traversalContextUnpacker;
  private readonly GrasshopperColorUnpacker _colorUnpacker;
  private readonly GrasshopperMaterialUnpacker _materialUnpacker;

  public LocalToGlobalMapHandler(
    TraversalContextUnpacker traversalContextUnpacker,
    GrasshopperCollectionRebuilder collectionRebuilder,
    GrasshopperColorUnpacker colorUnpacker,
    GrasshopperMaterialUnpacker materialUnpacker
  )
  {
    _traversalContextUnpacker = traversalContextUnpacker;
    _colorUnpacker = colorUnpacker;
    _materialUnpacker = materialUnpacker;
    CollectionRebuilder = collectionRebuilder;
  }

  /// <summary>
  /// Converts atomic object from TraversalContext to SpeckleObjectWrapper.
  /// </summary>
  public void ConvertAtomicObject(TraversalContext atomicContext)
  {
    var obj = atomicContext.Current;
    var objId = obj.applicationId ?? obj.id;

    if (objId == null || ConvertedObjectsMap.ContainsKey(objId))
    {
      return;
    }

    try
    {
      List<(GeometryBase, Base)> converted = SpeckleConversionContext.ConvertToHost(obj);

      if (converted.Count == 0)
      {
        return;
      }

      var path = _traversalContextUnpacker.GetCollectionPath(atomicContext).ToList();

      // Always create collection - consumed objects will be cleaned up later
      var objectCollection = CollectionRebuilder.GetOrCreateSpeckleCollectionFromPath(
        path,
        _colorUnpacker,
        _materialUnpacker
      );

      // Extract name and properties
      SpecklePropertyGroupGoo propertyGroup = new();
      string name = "";

      if (obj is Speckle.Objects.Data.DataObject dataObject)
      {
        propertyGroup.CastFrom(dataObject.properties);
        name = dataObject.name;
      }
      else
      {
        if (obj[Constants.PROPERTIES_PROP] is Dictionary<string, object?> props)
        {
          propertyGroup.CastFrom(props);
        }

        if (obj[Constants.NAME_PROP] is string objName)
        {
          name = objName;
        }
      }

      foreach ((GeometryBase geometryBase, Base original) in converted)
      {
        var wrapper = new SpeckleObjectWrapper()
        {
          Base = original,
          Path = path.Select(p => p.name).ToList(),
          Parent = objectCollection,
          GeometryBase = geometryBase,
          Properties = propertyGroup,
          Name = name,
          Color = _colorUnpacker.Cache.TryGetValue(original.applicationId ?? "", out var cachedObjColor)
            ? cachedObjColor
            : null,
          Material = _materialUnpacker.Cache.TryGetValue(original.applicationId ?? "", out var cachedObjMaterial)
            ? cachedObjMaterial
            : null,
          ApplicationId = objId
        };

        // Always add to both map and collections
        ConvertedObjectsMap[objId] = wrapper;
        CollectionRebuilder.AppendSpeckleGrasshopperObject(wrapper, path, _colorUnpacker, _materialUnpacker);
      }
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      // TODO: throw?
    }
  }

  /// <summary>
  /// Converts block instances and definitions from traversal contexts into Grasshopper wrapper objects.
  /// Automatically handles cleanup of consumed objects from the collection hierarchy.
  /// </summary>
  /// <remarks>
  /// Deliberately handles both block conversion AND consumed object cleanup in a single operation.
  /// Too much, I know, BUT it ensures the cleanup always occurs immediately after block processing without
  /// requiring receive components to call a separate cleanup method in the correct order.
  /// </remarks>
  public void ConvertBlockInstances(
    IReadOnlyCollection<TraversalContext> blocks,
    IReadOnlyCollection<InstanceDefinitionProxy>? definitionProxies
  )
  {
    var blockUnpacker = new GrasshopperBlockUnpacker(_traversalContextUnpacker, _colorUnpacker, _materialUnpacker);

    // Get consumed object IDs from unpacker
    var consumedObjectIds = blockUnpacker.UnpackBlocks(
      blocks,
      definitionProxies,
      ConvertedObjectsMap,
      CollectionRebuilder
    );

    // Clean up consumed objects from collections
    CollectionRebuilder.RemoveConsumedObjects(consumedObjectIds);
  }
}
