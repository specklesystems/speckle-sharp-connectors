using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Grasshopper8.Parameters;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.Grasshopper8.HostApp;

public class GrasshopperRootObjectBuilder() : IRootObjectBuilder<SpeckleCollectionGoo>
{
  public Task<RootObjectBuilderResult> Build(
    IReadOnlyList<SpeckleCollectionGoo> input,
    SendInfo sendInfo,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken ct = default
  )
  {
    // TODO: Send info is used in other connectors to get the project ID to populate the SendConversionCache
    Console.WriteLine($"Send Info {sendInfo}");

    // set the input collection name to "Grasshopper Model" and version
    var rootModel = input[0].Value;
    rootModel.name = "Grasshopper Model";

    // reconstruct the input collection by substituting all of the objectgoos with base
    ReplaceAndRebuild(rootModel);

    // TODO: Not getting any conversion results yet
    var result = new RootObjectBuilderResult(rootModel, []);

    return Task.FromResult(result);
  }

  // POC: this send component assumes that the input collection contains SpeckleObjects that `already` have a populated base prop
  // For new geometry, they should be converted to SpeckleObjects when passed to a `Create Collection` node.
  // Create DataObject should also output SpeckleObject as a custom grasshopper data object.
  private void ReplaceAndRebuild(Collection c)
  {
    // Iterate over the current collection's elements
    for (int i = 0; i < c.elements.Count; i++)
    {
      var element = c.elements[i];

      if (element is Collection collection)
      {
        // If it's a Collection, recursively replace its elements
        ReplaceAndRebuild(collection);
      }
      else if (element is SpeckleObject so)
      {
        // If it's not a Collection, replace the non-Collection element
        c.elements[i] = so.Base;
      }
    }
  }
}
