namespace Speckle.Connectors.Utils.Operations;

public record ReceiveInfo(
  string AccountId,
  Uri ServerUrl,
  string ProjectId,
  string ProjectName,
  string ModelId,
  string ModelName,
  string SelectedVersionId,
  string SourceApplication
);
