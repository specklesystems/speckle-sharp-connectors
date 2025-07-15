using Speckle.InterfaceGenerator;
using Speckle.Sdk.Api;
using Speckle.Sdk.Api.GraphQL.Inputs;
using Speckle.Sdk.Credentials;

namespace Speckle.Connectors.Common.Operations;

[GenerateAutoInterface]
public class SendOperationVersionRecorder(IClientFactory clientFactory) : ISendOperationVersionRecorder
{
  public async Task<string> RecordVersion(
    string rootId,
    SendInfo sendInfo,
    Account account,
    CancellationToken ct,
    string? versionMessage = null
  )
  {
    using var apiClient = clientFactory.Create(account);
    var x = await apiClient
      .Version.Create(
        new CreateVersionInput(
          rootId,
          sendInfo.ModelId,
          sendInfo.ProjectId,
          sourceApplication: sendInfo.SourceApplication,
          message: versionMessage
        ),
        ct
      )
      .ConfigureAwait(true);
    return x.id;
  }
}
