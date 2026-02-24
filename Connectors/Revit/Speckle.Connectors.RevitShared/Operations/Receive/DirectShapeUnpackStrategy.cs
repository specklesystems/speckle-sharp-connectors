using Speckle.Connectors.Common.Instances;
using Speckle.Connectors.Common.Operations.Receive;
using Speckle.Objects.Data;

namespace Speckle.Connectors.Revit.Operations.Receive;

public class DirectShapeUnpackStrategy : RevitUnpackStrategyBase
{
  private readonly ILocalToGlobalUnpacker _localToGlobalUnpacker;

  public DirectShapeUnpackStrategy(ILocalToGlobalUnpacker localToGlobalUnpacker)
  {
    _localToGlobalUnpacker = localToGlobalUnpacker;
  }

  public override UnpackStrategyResult Unpack(RootObjectUnpackerResult unpackedRoot)
  {
    // 1. Build the parent map so we don't lose metadata
    var parentDataObjectMap = new Dictionary<string, DataObject>();
    PopulateParentDataObjectMap(unpackedRoot, parentDataObjectMap);

    // 2. Flatten everything, including instances
    var maps = _localToGlobalUnpacker.Unpack(unpackedRoot.DefinitionProxies, unpackedRoot.ObjectsToConvert.ToList());

    // 3. Filter out DataObjects to avoid converter crashes
    var cleanedMaps = FilterUnpackedDataObjects(maps);

    return new UnpackStrategyResult(cleanedMaps, null, parentDataObjectMap);
  }
}
