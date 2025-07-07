using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.GrasshopperShared.Operations.Receive;

internal sealed class GrasshopperCollectionRebuilder
{
  public SpeckleCollectionWrapper RootCollectionWrapper { get; }

  // a cache of collection path (no delimiter) to the speckle collection
  private readonly Dictionary<string, SpeckleCollectionWrapper> _cache = new();

  public GrasshopperCollectionRebuilder(Collection baseCollection)
  {
    RootCollectionWrapper = new SpeckleCollectionWrapper()
    {
      Base = new Collection(),
      Name = baseCollection.name,
      Color = null,
      Material = null,
      ApplicationId = baseCollection.applicationId ?? Guid.NewGuid().ToString(),
      Path = new() { baseCollection.name }
    };
  }

  public void AppendSpeckleGrasshopperObject(
    SpeckleObjectWrapper speckleGrasshopperObjectWrapper,
    List<Collection> collectionPath,
    GrasshopperColorUnpacker colorUnpacker,
    GrasshopperMaterialUnpacker materialUnpacker
  )
  {
    var collWrapper = GetOrCreateSpeckleCollectionFromPath(collectionPath, colorUnpacker, materialUnpacker);
    collWrapper.Elements.Add(speckleGrasshopperObjectWrapper);
  }

  public SpeckleCollectionWrapper GetOrCreateSpeckleCollectionFromPath(
    List<Collection> path,
    GrasshopperColorUnpacker colorUnpacker,
    GrasshopperMaterialUnpacker materialUnpacker
  )
  {
    // first check if cache already has this collection
    string fullPath = string.Concat(path);
    if (_cache.TryGetValue(fullPath, out SpeckleCollectionWrapper col))
    {
      return col;
    }

    // otherwise, iterate through the path and create speckle collections as needed
    SpeckleCollectionWrapper previousCollectionWrapper = RootCollectionWrapper;
    List<string> currentLayerPath = new();
    foreach (var collection in path)
    {
      var collectionName = collection.name;
      currentLayerPath.Add(collectionName);
      string key = string.Concat(currentLayerPath);

      // check cache
      if (_cache.TryGetValue(key, out SpeckleCollectionWrapper currentCol))
      {
        previousCollectionWrapper = currentCol;
        continue;
      }

      // create and cache if needed
      SpeckleCollectionWrapper newSpeckleCollectionWrapper =
        new()
        {
          Base = new Collection(),
          Name = collectionName,
          ApplicationId = collection.applicationId,
          Path = currentLayerPath,
          Color = colorUnpacker.Cache.TryGetValue(collection.applicationId ?? "", out var cachedCollectionColor)
            ? cachedCollectionColor
            : null,
          Material = materialUnpacker.Cache.TryGetValue(
            collection.applicationId ?? "",
            out var cachedCollectionMaterial
          )
            ? cachedCollectionMaterial
            : null,
        };

      if (collection[Constants.TOPOLOGY_PROP] is string topology)
      {
        newSpeckleCollectionWrapper.Topology = topology;
      }

      _cache[key] = newSpeckleCollectionWrapper;
      previousCollectionWrapper.Elements.Add(newSpeckleCollectionWrapper);
      previousCollectionWrapper = newSpeckleCollectionWrapper;
    }

    return previousCollectionWrapper;
  }

  /// <summary>
  /// Removes consumed objects from the collection hierarchy.
  /// Matches Rhino's pattern: createdObjectIds.RemoveWhere(id => consumedObjectIds.Contains(id))
  /// </summary>
  /// <param name="consumedObjectIds">Set of object IDs that have been consumed by block definitions</param>
  public void RemoveConsumedObjects(HashSet<string> consumedObjectIds)
  {
    if (consumedObjectIds.Count == 0)
    {
      return;
    }

    RemoveConsumedObjectsFromCollection(RootCollectionWrapper, consumedObjectIds);
  }

  private static void RemoveConsumedObjectsFromCollection(
    SpeckleCollectionWrapper collection,
    HashSet<string> consumedObjectIds
  )
  {
    // Remove consumed objects from this level
    collection.Elements.RemoveAll(element =>
      element is SpeckleObjectWrapper obj && obj.ApplicationId != null && consumedObjectIds.Contains(obj.ApplicationId)
    );

    // Recurse into child collections
    foreach (var childCollection in collection.Elements.OfType<SpeckleCollectionWrapper>())
    {
      RemoveConsumedObjectsFromCollection(childCollection, consumedObjectIds);
    }
  }
}
