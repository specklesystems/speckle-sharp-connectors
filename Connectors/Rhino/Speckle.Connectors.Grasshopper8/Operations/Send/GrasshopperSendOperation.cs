using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Grasshopper8.Parameters;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.Grasshopper8.Operations.Send;

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

    // reconstruct the input collection by substituting all of the objectgoos with base
    var collection = ReplaceAndRebuild(rootCollection);

    // TODO: Not getting any conversion results yet
    var result = new RootObjectBuilderResult(collection, []);

    return Task.FromResult(result);
  }

  /// <summary>
  /// Unwraps collection wrappers and object wrapppers.
  /// </summary>
  /// <param name="c"></param>
  /// <returns></returns>
  private Collection ReplaceAndRebuild(Collection c)
  {
    // Iterate over the current collection's elements
    var myCollection = new Collection() { name = c.name };

    if (c["topology"] is string topology)
    {
      myCollection["topology"] = topology;
    }

    for (int i = 0; i < c.elements.Count; i++)
    {
      var element = c.elements[i];
      if (element is SpeckleCollectionWrapper collectionWrapper)
      {
        var newCollection = new Collection
        {
          name = collectionWrapper.Collection.name,
          ["topology"] = collectionWrapper.Topology,
          elements = collectionWrapper.Collection.elements
        };
        var unwrapped = ReplaceAndRebuild(newCollection);
        myCollection.elements.Add(unwrapped);
      }
      else if (element is SpeckleObjectWrapper so)
      {
        // If it's not a Collection, replace the non-Collection element
        myCollection.elements.Add(so.Base);
      }
    }
    return myCollection;
  }
}
