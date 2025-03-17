using Speckle.Connectors.Common.Conversion;

namespace Speckle.Connectors.Common.Instances;

public record BakeResult(
  IReadOnlyCollection<string> CreatedInstanceIds,
  IReadOnlyCollection<string> ConsumedObjectIds,
  IReadOnlyCollection<ReceiveConversionResult> InstanceConversionResults
);
