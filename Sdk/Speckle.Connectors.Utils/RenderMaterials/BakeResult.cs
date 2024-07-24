using Speckle.Connectors.Utils.Conversion;

namespace Speckle.Connectors.Utils.RenderMaterials;

public record BakeResult(
  Dictionary<string, int> MaterialIdMap,
  List<ReceiveConversionResult> InstanceConversionResults
);
