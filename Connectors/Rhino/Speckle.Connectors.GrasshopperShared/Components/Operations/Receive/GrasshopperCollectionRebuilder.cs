using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.GrasshopperShared.Components.Operations.Receive;

internal sealed class GrasshopperCollectionRebuilder
{
  public SpeckleCollectionWrapper RootCollectionWrapper { get; }

  // a cache of collection path (no delimiter) to the speckle collection
  private readonly Dictionary<string, SpeckleCollectionWrapper> _cache = new();

  public GrasshopperCollectionRebuilder(Collection baseCollection)
  {
    Collection newCollection = new() { name = baseCollection.name, applicationId = baseCollection.applicationId };
    RootCollectionWrapper = new SpeckleCollectionWrapper(newCollection, new() { baseCollection.name }, null);
  }

  public void AppendSpeckleGrasshopperObject(
    SpeckleObjectWrapper speckleGrasshopperObjectWrapper,
    List<Collection> collectionPath,
    GrasshopperColorBaker colorBaker
  )
  {
    // add the object color
    speckleGrasshopperObjectWrapper.Color = colorBaker.Cache.TryGetValue(
      speckleGrasshopperObjectWrapper.applicationId ?? "",
      out var cachedColor
    )
      ? cachedColor.Item1
      : null;

    var collection = GetOrCreateSpeckleCollectionFromPath(collectionPath, colorBaker);
    collection.Collection.elements.Add(speckleGrasshopperObjectWrapper);
  }

  public SpeckleCollectionWrapper GetOrCreateSpeckleCollectionFromPath(
    List<Collection> path,
    GrasshopperColorBaker colorBaker
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
      Collection newCollection = new() { name = collectionName, applicationId = collection.applicationId };
      SpeckleCollectionWrapper newSpeckleCollectionWrapper =
        new(newCollection, currentLayerPath, null)
        {
          // get the collection color
          Color = colorBaker.Cache.TryGetValue(collection.applicationId ?? "", out var cachedCollectionColor)
            ? cachedCollectionColor.Item1
            : null
        };
      ;

      if (collection["topology"] is string topology)
      {
        newSpeckleCollectionWrapper.Topology = topology;
        newCollection["topology"] = topology;
      }

      _cache[key] = newSpeckleCollectionWrapper;
      previousCollectionWrapper.Collection.elements.Add(newSpeckleCollectionWrapper);

      previousCollectionWrapper = newSpeckleCollectionWrapper;
    }

    return previousCollectionWrapper;
  }
}
