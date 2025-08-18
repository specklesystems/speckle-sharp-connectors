using Speckle.Importers.JobProcessor.Domain;
using Speckle.Importers.Rhino;
using Speckle.Sdk.Api;
using Version = Speckle.Sdk.Api.GraphQL.Models.Version;

namespace Speckle.Importers.JobProcessor.JobHandlers;

internal sealed class RhinoJobHandler : IJobHandler
{
  public async Task<Version> ProcessJob(FileimportJob job, IClient client, CancellationToken cancellationToken)
  {
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

      return await Importer.Import(
        targetFilePath,
        job.Payload.ProjectId,
        job.Payload.ModelId,
        client.Account,
        cancellationToken
      );
    }
    finally
    {
      Directory.Delete(directory.FullName, true);
    }
  }
}
