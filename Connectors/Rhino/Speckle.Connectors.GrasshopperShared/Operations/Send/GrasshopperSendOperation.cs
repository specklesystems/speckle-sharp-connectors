using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.GrasshopperShared.Operations.Send;

public class GrasshopperRootObjectBuilder() : IRootObjectBuilder<SpeckleCollectionWrapperGoo>
{
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

    // create unpackers for colors and render materials
    GrasshopperColorUnpacker colorUnpacker = new();

    // reconstruct the input collection by substituting all of the objectgoos with base
    Collection collection = ReplaceAndRebuild(rootCollection, colorUnpacker);

    // add proxies
    collection[ProxyKeys.COLOR] = colorUnpacker.ColorProxies.Values.ToList();

    // TODO: Not getting any conversion results yet
    var result = new RootObjectBuilderResult(collection, []);

    return Task.FromResult(result);
  }

  /// <summary>
  /// Unwraps collection wrappers and object wrapppers.
  /// Also unpacks colors into proxies while unwrapping.
  /// </summary>
  /// <param name="c"></param>
  /// <returns></returns>
  private Collection ReplaceAndRebuild(Collection c, GrasshopperColorUnpacker colorUnpacker)
  {
    // Iterate over the current collection's elements
    var myCollection = new Collection() { name = c.name };

    if (c["topology"] is string topology)
    {
      myCollection["topology"] = topology;
    }

    for (int i = 0; i < c.elements.Count; i++)
    {
      Base element = c.elements[i];
      if (element is SpeckleCollectionWrapper collectionWrapper)
      {
        // create an application id for this collection if none exists. This will be used for color and render material proxies
        string appId = collectionWrapper.applicationId ?? collectionWrapper.GetSpeckleApplicationId();
        var newCollection = new Collection
        {
          name = collectionWrapper.Collection.name,
          ["topology"] = collectionWrapper.Topology,
          elements = collectionWrapper.Collection.elements,
          applicationId = appId
        };

        // unpack color and render material
        colorUnpacker.ProcessColor(appId, collectionWrapper.Color);

        var unwrapped = ReplaceAndRebuild(newCollection, colorUnpacker);
        myCollection.elements.Add(unwrapped);
      }
      else if (element is SpeckleObjectWrapper so)
      {
        // unpack color and render material
        colorUnpacker.ProcessColor(so.applicationId, so.Color);

        // If it's not a Collection, replace the non-Collection element
        myCollection.elements.Add(so.Base);
      }
    }
    return myCollection;
  }
}
