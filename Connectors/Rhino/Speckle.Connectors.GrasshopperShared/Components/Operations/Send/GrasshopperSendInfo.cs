using Speckle.Connectors.Common.Operations;

namespace Speckle.Connectors.GrasshopperShared.Components.Operations.Send;

public record GrasshopperSendInfo(
  string AccountId,
  Uri ServerUrl,
  string? WorkspaceId,
  string ProjectId,
  string ModelId,
  string SourceApplication
) : SendInfo(AccountId, ServerUrl, ProjectId, ModelId, SourceApplication);
