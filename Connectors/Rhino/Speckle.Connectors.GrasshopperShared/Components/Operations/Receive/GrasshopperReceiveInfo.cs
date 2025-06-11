using Speckle.Connectors.Common.Operations;

namespace Speckle.Connectors.GrasshopperShared.Components.Operations.Receive;

public record GrasshopperReceiveInfo(
  string AccountId,
  Uri ServerUrl,
  string? WorkspaceId,
  string ProjectId,
  string ProjectName,
  string ModelId,
  string ModelName,
  string SelectedVersionId,
  string SourceApplication,
  string? SelectedVersionUserId
) : ReceiveInfo(AccountId, ServerUrl, ProjectId, ProjectName, ModelId, ModelName, SelectedVersionId, SourceApplication);
