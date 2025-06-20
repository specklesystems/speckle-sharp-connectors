using Speckle.Sdk.Credentials;

namespace Speckle.Connectors.Common.Operations;

public record ReceiveInfo(
  Account Account,
  string ProjectId,
  string ProjectName,
  string ModelId,
  string ModelName,
  string SelectedVersionId,
  string SourceAppilcation
);
