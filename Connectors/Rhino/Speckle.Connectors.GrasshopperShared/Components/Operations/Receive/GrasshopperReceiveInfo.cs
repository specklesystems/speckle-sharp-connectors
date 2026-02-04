using Speckle.Connectors.Common.Operations;
using Speckle.Sdk.Credentials;
using Version = Speckle.Sdk.Api.GraphQL.Models.Version;

namespace Speckle.Connectors.GrasshopperShared.Components.Operations.Receive;

/// <param name="Account"></param>
/// <param name="WorkspaceId"></param>
/// <param name="ProjectId"></param>
/// <param name="ProjectName"></param>
/// <param name="ModelId"></param>
/// <param name="ModelName"></param>
/// <param name="SelectedVersionId"></param>
/// <param name="SourceApplication">See <see cref="Version.sourceApplication"/></param>
/// <param name="ReceivingApplicationSlug">Slug of the application doing the receiving (i.e. the current host app)</param>
/// <param name="SelectedVersionUserId"></param>
public record GrasshopperReceiveInfo(
  Account Account,
  string? WorkspaceId,
  string ProjectId,
  string ProjectName,
  string ModelId,
  string ModelName,
  string SelectedVersionId,
  string SourceApplication,
  string ReceivingApplicationSlug,
  string? SelectedVersionUserId
) : ReceiveInfo(Account, ProjectId, ProjectName, ModelId, ModelName, SelectedVersionId, ReceivingApplicationSlug);
