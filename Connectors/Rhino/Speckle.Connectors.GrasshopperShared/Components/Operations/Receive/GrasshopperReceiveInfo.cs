using Speckle.Connectors.Common.Operations;
using Speckle.Sdk.Credentials;

namespace Speckle.Connectors.GrasshopperShared.Components.Operations.Receive;

public record GrasshopperReceiveInfo(
  Account Account,
  string? WorkspaceId,
  string ProjectId,
  string ProjectName,
  string ModelId,
  string ModelName,
  string SelectedVersionId,
  string SourceApplication,
  string? SelectedVersionUserId
) : ReceiveInfo(Account, ProjectId, ProjectName, ModelId, ModelName, SelectedVersionId, SourceApplication);
