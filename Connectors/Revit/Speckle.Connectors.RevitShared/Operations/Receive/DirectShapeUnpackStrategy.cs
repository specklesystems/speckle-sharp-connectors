using Speckle.Connectors.Common.Instances;
using Speckle.Connectors.Common.Operations.Receive;

namespace Speckle.Connectors.Revit.Operations.Receive;

public class DirectShapeUnpackStrategy : IRevitUnpackStrategy
{
  private readonly ILocalToGlobalUnpacker _localToGlobalUnpacker;

  public DirectShapeUnpackStrategy(ILocalToGlobalUnpacker localToGlobalUnpacker)
  {
    _localToGlobalUnpacker = localToGlobalUnpacker;
  }

  public UnpackStrategyResult Unpack(RootObjectUnpackerResult unpackedRoot)
  {
    // Flatten everything, including instances
    var maps = _localToGlobalUnpacker.Unpack(unpackedRoot.DefinitionProxies, unpackedRoot.ObjectsToConvert.ToList());

    // Return maps, null for families, and an empty dictionary for the parent map
    return new UnpackStrategyResult(maps, null, []);
  }
}
