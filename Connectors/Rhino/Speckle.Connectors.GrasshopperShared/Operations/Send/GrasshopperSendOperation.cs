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

    // TODO:create packers for colors and render materials
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
        colorPacker.ProcessColor(appId, collectionWrapper.Color);
        materialPacker.ProcessMaterial(appId, collectionWrapper.Material);

        var unwrapped = ReplaceAndRebuild(newCollection, colorPacker, materialPacker);
        myCollection.elements.Add(unwrapped);
      }
      else if (element is SpeckleObjectWrapper so)
      {
        // unpack color and render material
        colorPacker.ProcessColor(so.applicationId, so.Color);
        materialPacker.ProcessMaterial(so.applicationId, so.Material);

        // If it's not a Collection, replace the non-Collection element
        myCollection.elements.Add(so.Base);
      }
    }
    return myCollection;
  }
}
