using Speckle.InterfaceGenerator;
using Speckle.Sdk.Api;
using Speckle.Sdk.Api.GraphQL.Inputs;
using Speckle.Sdk.Credentials;

namespace Speckle.Connectors.Common.Operations;

[GenerateAutoInterface]
public class SendOperationVersionRecorder(IClientFactory clientFactory) : ISendOperationVersionRecorder
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
          sourceApplication: sendInfo.SourceApplication
        ),
        ct
      )
      .ConfigureAwait(true);
    return x.id;
  }
}
