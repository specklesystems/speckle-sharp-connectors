using Speckle.Sdk.Credentials;

namespace Speckle.Connectors.Common.Operations;

/// <param name="Account"></param>
/// <param name="ProjectId"></param>
/// <param name="ProjectName"></param>
/// <param name="ModelId"></param>
/// <param name="ModelName"></param>
/// <param name="SelectedVersionId"></param>
/// <param name="ReceivingApplicationSlug">Slug of the application doing the receiving (i.e. the current host app)</param>
public record ReceiveInfo(
  Account Account,
  string ProjectId,
  string ProjectName,
  string ModelId,
  string ModelName,
  string SelectedVersionId,
  string ReceivingApplicationSlug
);
