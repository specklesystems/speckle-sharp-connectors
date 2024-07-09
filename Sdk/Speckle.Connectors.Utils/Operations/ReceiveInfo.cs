namespace Speckle.Connectors.Utils.Operations;

public record ReceiveInfo(
  string AccountId,
  Uri ServerUrl,
  string ProjectId,
  string ProjectName,
  string ModelName,
  string SelectedVersionId
);
