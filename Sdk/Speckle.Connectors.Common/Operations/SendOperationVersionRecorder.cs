using Speckle.InterfaceGenerator;
using Speckle.Sdk.Api;
using Speckle.Sdk.Api.GraphQL.Inputs;
using Speckle.Sdk.Api.GraphQL.Models;
using Version = Speckle.Sdk.Api.GraphQL.Models.Version;

namespace Speckle.Connectors.Common.Operations;

[GenerateAutoInterface]
public class SendOperationVersionRecorder : ISendOperationVersionRecorder
{
  public async Task<Version> RecordVersion(
    string rootId,
    Ingest ingest,
    string? versionMessage,
    IClient apiClient,
    CancellationToken ct
  )
  {
    // var x = await apiClient
    //   .Version.Create(
    //     new CreateVersionInput(
    //       rootId,
    //       modelId,
    //       projectId,
    //       sourceApplication: sourceApplication,
    //       message: versionMessage
    //     ),
    //     ct
    //   )
    //   .ConfigureAwait(true);

    var x = await apiClient
      .Ingest.End(
        new IngestFinishInput(
          ingest.id,
          versionMessage,
          rootId,
          ingest.projectId
        ),
        ct
      )
      .ConfigureAwait(true);
    return x;
  }
}
