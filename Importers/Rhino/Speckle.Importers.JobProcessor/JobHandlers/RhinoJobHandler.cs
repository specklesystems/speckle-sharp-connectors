using Microsoft.Extensions.Logging;
using Speckle.Importers.JobProcessor.Domain;
using Speckle.Importers.Rhino;
using Speckle.Sdk.Api;
using Version = Speckle.Sdk.Api.GraphQL.Models.Version;

namespace Speckle.Importers.JobProcessor.JobHandlers;

internal sealed class RhinoJobHandler(ILogger<RhinoJobHandler> logger) : IJobHandler
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
      try
      {
        await Cleanup(directory.FullName);
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
      {
        logger.LogError(ex, "Failed to cleanup file");
      }
    }
  }

  private static async Task Cleanup(string path)
  {
    //Some weird cases where *something* is keeping a lock on the file, this *may* fix things...
    await Task.Delay(100);
    GC.Collect();
    GC.WaitForPendingFinalizers();
    Directory.Delete(path, true);
  }
}
