using Speckle.InterfaceGenerator;
using Speckle.Sdk.Api;
using Speckle.Sdk.Api.GraphQL.Inputs;
using Speckle.Sdk.Credentials;

namespace Speckle.Connectors.Common.Operations;

[GenerateAutoInterface]
public class SendOperationVersionRecorder(IClientFactory clientFactory, IAccountManager accountManager)
  : ISendOperationVersionRecorder
{
  public async Task<string> RecordVersion(
    string rootId,
    string modelId,
    string projectId,
    string sourceApplication,
    Uri serverUrl,
    string token,
    CancellationToken ct
  )
  {
    Account account =
      new()
      {
        token = token,
        serverInfo = await accountManager.GetServerInfo(serverUrl, ct),
        userInfo = await accountManager.GetUserInfo(token, serverUrl, ct),
      };
    using var apiClient = clientFactory.Create(account);
    var x = await apiClient
      .Version.Create(new CreateVersionInput(rootId, modelId, projectId, sourceApplication: sourceApplication), ct)
      .ConfigureAwait(true);
    return x.id;
  }
}
