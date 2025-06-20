using Speckle.Connectors.Common.Operations;
using Speckle.Sdk.Credentials;

namespace Speckle.Connectors.GrasshopperShared.Components.Operations.Send;

public record GrasshopperSendInfo(
  Account Account,
  string? WorkspaceId,
  string ProjectId,
  string ModelId
) : SendInfo(Account, ProjectId, ModelId);
