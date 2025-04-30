using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using DataObject = Speckle.Objects.Data.DataObject;

namespace Speckle.Connectors.GrasshopperShared.Operations.Send;

public class GrasshopperRootObjectBuilder() : IRootObjectBuilder<SpeckleCollectionWrapperGoo>
{
  // Keeps track of the wrapper applicationId of processed objects for send.
  // This is used to keep track of the following situations:
  // 1 - objects with the same name, properties, and application id are packaged into a data object. this can happen when receiving data objects.
  // 2 - mutated objects (put into a diff collection) that originally came from the same display value should be assigned a new application id.
  // note: if any original objects that came from the same display value were mutated in geometry, props, or name, in the create speckle object node, they will already be assigned a new appId
  private readonly Dictionary<string, List<SpeckleObjectWrapper>> _applicationIdCache = new();

  public Task<RootObjectBuilderResult> Build(
    IReadOnlyList<SpeckleCollectionWrapperGoo> input,
    SendInfo sendInfo,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken ct = default
  )
  {
    // TODO: Send info is used in other connectors to get the project ID to populate the SendConversionCache
    Console.WriteLine($"Send Info {sendInfo}");

    // set the input collection name to "Grasshopper Model"
    var rootCollection = new Collection { name = "Grasshopper model", elements = input[0].Value.Collection.elements };

    // create packers for colors and render materials
    GrasshopperColorPacker colorPacker = new();
    GrasshopperMaterialPacker materialPacker = new();

    // reconstruct the input collection by substituting all of the objectgoos with base
    Collection collection = ReplaceAndRebuild(rootCollection, colorPacker, materialPacker);

    // add proxies
    collection[ProxyKeys.COLOR] = colorPacker.ColorProxies.Values.ToList();
    collection[ProxyKeys.MATERIAL] = materialPacker.RenderMaterialProxies.Values.ToList();

    // TODO: Not getting any conversion results yet
    var result = new RootObjectBuilderResult(collection, []);

    return Task.FromResult(result);
  }

  /// <summary>
  /// Unwraps collection wrappers and object wrapppers.
  /// Also packs colors and Render Materials into proxies while unwrapping.
  /// POC: this probably doesn't handle dataobjects well (coming from revit, where we've exploded each display value and kept the original base)
  /// </summary>
  /// <param name="c"></param>
  /// <returns></returns>
  private Collection ReplaceAndRebuild(
    Collection c,
    GrasshopperColorPacker colorPacker,
    GrasshopperMaterialPacker materialPacker
  )
  {
    // Iterate over the current collection's elements
    var myCollection = new Collection() { name = c.name };

    if (c["topology"] is string topology)
    {
      myCollection["topology"] = topology;
    }

    HashSet<string> collectionObjectIds = new();
    for (int i = 0; i < c.elements.Count; i++)
    {
      Base element = c.elements[i];
      if (element is SpeckleCollectionWrapper collectionWrapper)
      {
        // create an application id for this collection if none exists. This will be used for color and render material proxies
        string appId = collectionWrapper.applicationId ?? collectionWrapper.GetSpeckleApplicationId();
        Collection newCollection =
          new()
          {
            name = collectionWrapper.Collection.name,
            ["topology"] = collectionWrapper.Topology,
            elements = collectionWrapper.Collection.elements,
            applicationId = appId
          };

        // unpack color and render material
        colorPacker.ProcessColor(appId, collectionWrapper.Color);
        materialPacker.ProcessMaterial(appId, collectionWrapper.Material);

        var unwrapped = ReplaceAndRebuild(newCollection, colorPacker, materialPacker);
        myCollection.elements.Add(unwrapped);
      }
      else if (element is SpeckleObjectWrapper so)
      {
        // process the object first. This may result in application id mutations, so this must be done before processing color and materials.
        ProcessObjectWrapper(so, ref collectionObjectIds);

        // unpack color and render material
        colorPacker.ProcessColor(so.Base.applicationId, so.Color);
        materialPacker.ProcessMaterial(so.Base.applicationId, so.Material);
      }
    }

    // now package all corresponding wrappers of app ids in the hashset into dataobjects, and add to collection
    foreach (string collectionObjectId in collectionObjectIds)
    {
      if (_applicationIdCache.TryGetValue(collectionObjectId, out var wrappers))
      {
        // create a data object for this id.
        // should be able to use the name and props of first wrapper since this should be the same for all wrappers after processing
        Dictionary<string, object?> props = new();
        wrappers.First().Properties.CastTo<Dictionary<string, object?>>(ref props);

        DataObject dataObject =
          new()
          {
            displayValue = wrappers.Select(o => o.Base).ToList(),
            name = wrappers.First().Name,
            properties = props,
            applicationId = collectionObjectId
          };

        myCollection.elements.Add(dataObject);
      }
    }

    return myCollection;
  }

  // will cache the object wrappers and group them by similarity.
  private void ProcessObjectWrapper(SpeckleObjectWrapper objectWrapper, ref HashSet<string> processedIds)
  {
    // check each of the hashset keys in the cache for similarity to this objectwrapper
    foreach (string processedId in processedIds)
    {
      if (_applicationIdCache.TryGetValue(processedId, out List<SpeckleObjectWrapper> wrappers))
      {
        // check if the object wrapper smells like existing object wrappers.
        if (objectWrapper.SmellsLike(wrappers.FirstOrDefault()))
        {
          objectWrapper.WrapperGuid = processedId;
          _applicationIdCache[processedId].Add(objectWrapper);
          return;
        }
      }
    }

    // if no similar wrappers found, create a new appid and store this.
    string newId = Guid.NewGuid().ToString();
    objectWrapper.WrapperGuid = newId;
    processedIds.Add(newId);
    _applicationIdCache.Add(newId, new() { objectWrapper });
    return;
  }
}
