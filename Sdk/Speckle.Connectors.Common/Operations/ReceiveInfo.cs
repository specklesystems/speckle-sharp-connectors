namespace Speckle.Connectors.Common.Operations;

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
