using Speckle.InterfaceGenerator;
using Speckle.Sdk;
using Speckle.Sdk.Api;
using Speckle.Sdk.Credentials;

namespace Speckle.Connectors.Common.Operations;

[GenerateAutoInterface]
public class ReceiveVersionRetriever(IClientFactory clientFactory, ISpeckleApplication application) : IReceiveVersionRetriever
{
  public async Task<Speckle.Sdk.Api.GraphQL.Models.Version> GetVersion(
    Account account,
    ReceiveInfo receiveInfo,
    CancellationToken cancellationToken
  )
  {
    using var apiClient = clientFactory.Create(account);

    var version = await apiClient.Version.Get(receiveInfo.SelectedVersionId, receiveInfo.ProjectId, cancellationToken);
    return version;
  }

  public async Task VersionReceived(
    Account account,
    Speckle.Sdk.Api.GraphQL.Models.Version version,
    ReceiveInfo receiveInfo,
    CancellationToken cancellationToken
  )
  {
    using var apiClient = clientFactory.Create(account);

    await apiClient.Version.Received(
      new(version.id, receiveInfo.ProjectId, application.Slug),
      cancellationToken
    );
  }
}
