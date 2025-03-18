using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Grasshopper8.Parameters;

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
    rootModel["version"] = 3;

    // TODO: Not getting any conversion results yet
    var result = new RootObjectBuilderResult(rootModel, []);

    return Task.FromResult(result);
  }
}
