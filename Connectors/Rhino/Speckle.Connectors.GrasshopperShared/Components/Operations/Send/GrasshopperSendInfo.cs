using Speckle.Connectors.Common.Operations;
using Speckle.Sdk.Credentials;

namespace Speckle.Connectors.GrasshopperShared.Components.Operations.Send;

public record GrasshopperSendInfo(
  Account Account,
  string? WorkspaceId,
  string ProjectId,
  string ModelId,
  string SourceApplication
) : SendInfo(Account, ProjectId, ModelId, SourceApplication);
