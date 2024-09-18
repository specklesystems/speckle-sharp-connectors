using Speckle.Connectors.Common.Conversion;

namespace Speckle.Connectors.Common.Instances;

public record BakeResult(
  List<string> CreatedInstanceIds,
  List<string> ConsumedObjectIds,
  List<ReceiveConversionResult> InstanceConversionResults
);
