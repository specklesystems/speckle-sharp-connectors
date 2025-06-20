using Speckle.InterfaceGenerator;
using Speckle.Sdk;
using Speckle.Sdk.Api;
using Speckle.Sdk.Api.GraphQL.Inputs;
using Speckle.Sdk.Credentials;

namespace Speckle.Connectors.Common.Operations;

[GenerateAutoInterface]
public class SendOperationVersionRecorder(IClientFactory clientFactory, ISpeckleApplication application) : ISendOperationVersionRecorder
{
  public async Task<string> RecordVersion(string rootId, SendInfo sendInfo, Account account, CancellationToken ct)
  {
    using var apiClient = clientFactory.Create(account);
    var x = await apiClient
      .Version.Create(
        new CreateVersionInput(
          rootId,
          sendInfo.ModelId,
          sendInfo.ProjectId,
          sourceApplication: application.ApplicationAndVersion
        ),
        ct
      )
      .ConfigureAwait(true);
    return x.id;
  }
}
