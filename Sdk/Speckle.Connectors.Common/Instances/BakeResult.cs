using Speckle.Connectors.Utils.Conversion;

namespace Speckle.Connectors.Utils.Instances;

public record BakeResult(
  List<string> CreatedInstanceIds,
  List<string> ConsumedObjectIds,
  List<ReceiveConversionResult> InstanceConversionResults
);
