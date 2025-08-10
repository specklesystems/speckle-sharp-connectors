using Speckle.InterfaceGenerator;
using Speckle.Sdk.Api;
using Speckle.Sdk.Api.GraphQL.Inputs;
using Speckle.Sdk.Credentials;
using Version = Speckle.Sdk.Api.GraphQL.Models.Version;

namespace Speckle.Connectors.Common.Operations;

[GenerateAutoInterface]
public class SendOperationVersionRecorder(IClientFactory clientFactory) : ISendOperationVersionRecorder
{
  public async Task<Version> RecordVersion(
    string rootId,
    string modelId,
    string projectId,
    string sourceApplication,
    string? versionMessage,
    Account account,
    CancellationToken ct
  )
  {
    using var apiClient = clientFactory.Create(account);
    var x = await apiClient
      .Version.Create(
        new CreateVersionInput(
          rootId,
          modelId,
          projectId,
          sourceApplication: sourceApplication,
          message: versionMessage
        ),
        ct
      )
      .ConfigureAwait(true);
    return x;
  }
}
