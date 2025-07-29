using Speckle.Importers.JobProcessor.Domain;
using Speckle.Importers.Rhino;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.Transports.ServerUtils;
using Version = Speckle.Sdk.Api.GraphQL.Models.Version;

namespace Speckle.Importers.JobProcessor;

public sealed class RhinoJobHandler(Importer importer, IAccountFactory accountFactory, ServerApi serverApi)
  : IJobHandler
{
  public async Task<Version> ProcessJob(FileimportJob job, CancellationToken cancellationToken)
  {
    //todo: download blob
    serverApi.CancellationToken = cancellationToken;
    await serverApi.DownloadBlobs(job.Payload.ProjectId, [job.Payload.BlobId], null);

    var filePath = "";
    var account = await accountFactory.CreateAccount(
      job.Payload.ServerUrl,
      job.Payload.Token,
      cancellationToken: cancellationToken
    );

    return await importer.Import(filePath, job.Payload.ProjectId, job.Payload.ModelId, account, cancellationToken);
  }
}
