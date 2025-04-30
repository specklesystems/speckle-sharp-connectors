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
    Collection newCollection = new() { name = baseCollection.name };
    RootCollectionWrapper = new SpeckleCollectionWrapper(new() { baseCollection.name })
    {
      Base = newCollection,
      Color = null,
      Material = null,
      ApplicationId = baseCollection.applicationId ?? Guid.NewGuid().ToString(),
      WrapperGuid = null,
    };
  }

  public void AppendSpeckleGrasshopperObject(
    SpeckleObjectWrapper speckleGrasshopperObjectWrapper,
    List<Collection> collectionPath,
    GrasshopperColorUnpacker colorUnpacker,
    GrasshopperMaterialUnpacker materialUnpacker
  )
  {
    // add the object color and material
    speckleGrasshopperObjectWrapper.Color = colorUnpacker.Cache.TryGetValue(
      speckleGrasshopperObjectWrapper.Base.applicationId ?? "",
      out var cachedColor
    )
      ? cachedColor
      : null;

    speckleGrasshopperObjectWrapper.Material = materialUnpacker.Cache.TryGetValue(
      speckleGrasshopperObjectWrapper.Base.applicationId ?? "",
      out var cachedMaterial
    )
      ? cachedMaterial
      : null;

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
      Collection newCollection = new() { name = collectionName, applicationId = collection.applicationId };
      SpeckleCollectionWrapper newSpeckleCollectionWrapper =
        new(currentLayerPath)
        {
          Base = newCollection,
          // get the collection color and material
          Color = colorUnpacker.Cache.TryGetValue(collection.applicationId ?? "", out var cachedCollectionColor)
            ? cachedCollectionColor
            : null,
          Material = materialUnpacker.Cache.TryGetValue(
            collection.applicationId ?? "",
            out var cachedCollectionMaterial
          )
            ? cachedCollectionMaterial
            : null,
          WrapperGuid = null,
        };

      if (collection["topology"] is string topology)
      {
        newSpeckleCollectionWrapper.Topology = topology;
        newCollection["topology"] = topology;
      }

      _cache[key] = newSpeckleCollectionWrapper;
      previousCollectionWrapper.Elements.Add(newSpeckleCollectionWrapper);
      previousCollectionWrapper = newSpeckleCollectionWrapper;
    }

    return previousCollectionWrapper;
  }
}
