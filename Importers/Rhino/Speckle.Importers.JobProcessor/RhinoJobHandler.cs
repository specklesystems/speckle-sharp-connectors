using Speckle.Importers.JobProcessor.Domain;
using Speckle.Importers.Rhino;
using Speckle.Sdk.Api;
using Speckle.Sdk.Credentials;
using Version = Speckle.Sdk.Api.GraphQL.Models.Version;

namespace Speckle.Importers.JobProcessor;

public sealed class RhinoJobHandler(Importer importer, IAccountFactory accountFactory, IClientFactory clientFactory)
  : IJobHandler
{
  public async Task<Version> ProcessJob(FileimportJob job, CancellationToken cancellationToken)
  {
    var account = await accountFactory.CreateAccount(
      new("http://127.0.0.1"), // job.Payload.ServerUrl, //TODO: we should grab serverUrl from the job. But currently it reports the docker network url, which is no bueno
      job.Payload.Token,
      cancellationToken: cancellationToken
    );

    using var client = clientFactory.Create(account);
    var directory = Directory.CreateTempSubdirectory("speckle-file-import");
    try
    {
      string targetFilePath = $"{directory.FullName}/{job.Payload.JobId}.{job.Payload.FileType}";
      await client.FileImport.DownloadFile(
        job.Payload.ProjectId,
        job.Payload.BlobId,
        targetFilePath,
        null,
        cancellationToken
      );

      return await importer.Import(
        targetFilePath,
        job.Payload.ProjectId,
        job.Payload.ModelId,
        account,
        cancellationToken
      );
    }
    finally
    {
      Directory.Delete(directory.FullName, true);
    }
  }
}
