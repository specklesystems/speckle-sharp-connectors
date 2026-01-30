using Speckle.Connectors.Common.Operations;
using Speckle.Sdk.Api;

namespace Speckle.Connectors.GrasshopperShared.Components.Operations.Send;

public record GrasshopperSendInfo(IClient Client, string? WorkspaceId, string ProjectId, string ModelId)
  : SendInfo(Client, ProjectId, ModelId);
